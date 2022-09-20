namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.IO
open System.Net.Http
open System.Linq

open NBitcoin
open DotNetLightning.Serialization
open DotNetLightning.Serialization.Msgs
open DotNetLightning.Utils
open QuikGraph
open QuikGraph.Algorithms

open ResultUtils.Portability
open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks


/// Information about hop in multi-hop payments
/// Used in edge cost calculation and onion packet creation.
/// Cannot reuse RoutingGrpahEdge because route can contain extra hops
/// through private channel (ExtraHop type)
type internal IRoutingHopInfo =
    abstract NodeId: NodeId
    abstract ShortChannelId: ShortChannelId
    abstract FeeBaseMSat: LNMoney
    abstract FeeProportionalMillionths: uint32
    abstract CltvExpiryDelta: uint32
    abstract HTLCMaximumMSat: LNMoney option
    abstract HTLCMinimumMSat: LNMoney


type internal RoutingGrpahEdge = 
    {
        Source : NodeId
        Target : NodeId
        ShortChannelId : ShortChannelId
        Update: UnsignedChannelUpdateMsg
    }
    interface IEdge<NodeId> with
        member this.Source = this.Source
        member this.Target = this.Target

    interface IRoutingHopInfo with
        override self.NodeId = self.Source
        override self.ShortChannelId = self.Update.ShortChannelId
        override self.FeeBaseMSat = self.Update.FeeBaseMSat
        override self.FeeProportionalMillionths = self.Update.FeeProportionalMillionths
        override self.CltvExpiryDelta = self.Update.CLTVExpiryDelta.Value |> uint32
        override self.HTLCMaximumMSat = self.Update.HTLCMaximumMSat
        override self.HTLCMinimumMSat = self.Update.HTLCMinimumMSat


type internal RoutingGraph = ArrayAdjacencyGraph<NodeId, RoutingGrpahEdge>


module private RoutingHeuristics =
    // code moslty from DotNetLightning
    let BLOCK_TIME_TWO_MONTHS = 8640us |> BlockHeightOffset16
    let CAPACITY_CHANNEL_LOW = LNMoney.Satoshis 1000L

    let CAPACITY_CHANNEL_HIGH =
        DotNetLightning.Channel.ChannelConstants.MAX_FUNDING_SATOSHIS.Satoshi
        |> LNMoney.Satoshis

    [<Literal>]
    let CLTV_LOW = 9L

    [<Literal>]
    let CLTV_HIGH = 2016

    let normalize(v, min, max) : double =
        if (v <= min) then
            0.00001
        else if (v > max) then
            0.99999
        else
            (v - min) / (max - min)

    // factors?
    let CltvDeltaFactor = 1.0
    let CapacityFactor = 1.0


module internal EdgeWeightCaluculation =
    // code is partly from DotNetLightning

    let nodeFee (baseFee: LNMoney) (proportionalFee: int64) (paymentAmount: LNMoney) =
        baseFee + LNMoney.Satoshis(decimal(paymentAmount.Satoshi * proportionalFee) / 1000000.0m)
        
    let edgeFeeCost (amountWithFees: LNMoney) (edge: IRoutingHopInfo) =
        let result =
            nodeFee
                edge.FeeBaseMSat 
                (int64 edge.FeeProportionalMillionths)
                amountWithFees
        // We can't have zero fee cost because it causes weight to be 0 regardless of expiry_delta
        LNMoney.Max(result, LNMoney.MilliSatoshis(1))

    /// Computes the weight for the given edge
    let edgeWeight (paymentAmount: LNMoney) (edge: IRoutingHopInfo) : float =
        let feeCost = float (edgeFeeCost paymentAmount edge).Value
        let channelCLTVDelta = edge.CltvExpiryDelta
        let edgeMaxCapacity =
            edge.HTLCMaximumMSat
            |> Option.defaultValue(RoutingHeuristics.CAPACITY_CHANNEL_LOW)
        if edgeMaxCapacity < paymentAmount then
            infinity // chanel capacity is too small, reject edge
        elif paymentAmount < edge.HTLCMinimumMSat then
            infinity // our payment is too small for the channel, reject edge
        else
            let capFactor =
                1.0 - RoutingHeuristics.normalize(
                        float edgeMaxCapacity.MilliSatoshi,
                        float RoutingHeuristics.CAPACITY_CHANNEL_LOW.MilliSatoshi,
                        float RoutingHeuristics.CAPACITY_CHANNEL_HIGH.MilliSatoshi)
            let cltvFactor =
                RoutingHeuristics.normalize(
                    float channelCLTVDelta,
                    float RoutingHeuristics.CLTV_LOW,
                    float RoutingHeuristics.CLTV_HIGH)
            let factor = 
                cltvFactor * RoutingHeuristics.CltvDeltaFactor 
                + capFactor * RoutingHeuristics.CapacityFactor
            factor * feeCost


module RapidGossipSyncer =
    
    let private RgsPrefix = [| 76uy; 68uy; 75uy; 1uy |]

    type internal CompactAnnouncment =
        {
            ChannelFeatures: Result<FeatureBits, FeatureError>
            ShortChannelId: ShortChannelId
            NodeId1: NodeId
            NodeId2: NodeId
        }

    type internal CompactChannelUpdate =
        {
            ShortChannelId: ShortChannelId
            CLTVExpiryDelta: uint16
            HtlcMinimumMSat: uint64
            FeeBaseMSat: uint32
            FeeProportionalMillionths: uint32
            HtlcMaximumMSat: uint64
        }

    type internal ChannelUpdates =
        {
            Forward: UnsignedChannelUpdateMsg option
            Backward: UnsignedChannelUpdateMsg option
        }
        static member Empty = { Forward = None; Backward = None }
    
        member self.With(update: UnsignedChannelUpdateMsg) =
            let isForward = (update.ChannelFlags &&& 1uy) = 0uy
            if isForward then
                match self.Forward with
                | Some(prevUpd) when update.Timestamp < prevUpd.Timestamp -> self
                | _ -> { self with Forward = Some(update) }
            else
                match self.Backward with
                | Some(prevUpd) when update.Timestamp < prevUpd.Timestamp -> self
                | _ -> { self with Backward = Some(update) }

        member self.Combine(other: ChannelUpdates) =
            let combine upd1opt upd2opt : UnsignedChannelUpdateMsg option =
                match upd1opt, upd2opt with
                | None, None -> None
                | Some(_), None -> upd1opt
                | None, Some(_) -> upd2opt
                | Some(upd1), Some(upd2) -> if upd1.Timestamp > upd2.Timestamp then upd1opt else upd2opt
            { Forward = combine self.Forward other.Forward; Backward = combine self.Backward other.Backward }


    /// see https://github.com/lightningdevkit/rust-lightning/tree/main/lightning-rapid-gossip-sync/#custom-channel-update
    module internal CustomChannelUpdateFlags =
        let Direction = 1uy
        let DisableCHannel = 2uy
        let HtlcMaximumMsat = 4uy
        let FeeProportionalMillionths = 8uy
        let FeeBaseMsat = 16uy
        let HtlcMinimumMsat = 32uy
        let CltvExpiryDelta = 64uy
        let IncrementalUpdate = 128uy
    
    
    type internal RoutingGraphData private(announcements: Set<CompactAnnouncment>, 
                                           updates: Map<ShortChannelId, ChannelUpdates>,
                                           lastSyncTimestamp: uint32,
                                           blacklistedChannels: Set<ShortChannelId>,
                                           routingGraph: RoutingGraph) =
        
        new() = RoutingGraphData(Set.empty, Map.empty, 0u, Set.empty, RoutingGraph(AdjacencyGraph()))

        member self.LastSyncTimestamp = lastSyncTimestamp

        member self.Graph = routingGraph

        member self.Update (newAnnouncements : seq<CompactAnnouncment>) 
                           (newUpdates: Map<ShortChannelId, ChannelUpdates>) 
                           (syncTimestamp: uint32) : RoutingGraphData =
            let announcements = 
                announcements 
                |> Set.union (newAnnouncements |> Set.ofSeq)
                |> Set.filter (fun ann -> not (blacklistedChannels |> Set.contains ann.ShortChannelId))
            
            let updates =
                if updates.IsEmpty then
                    newUpdates
                else
                    let mutable tmpUpdates = updates
                    newUpdates |> Map.iter (fun channelId newUpd ->
                        match tmpUpdates |> Map.tryFind channelId with
                        | Some upd ->
                            tmpUpdates <- tmpUpdates |> Map.add channelId (upd.Combine newUpd)
                        | None ->
                            tmpUpdates <- tmpUpdates |> Map.add channelId newUpd )
                    tmpUpdates

            let baseGraph = AdjacencyGraph<NodeId, RoutingGrpahEdge>()

            for ann in announcements do
                let updates = updates.[ann.ShortChannelId]
                    
                let addEdge source target (upd : UnsignedChannelUpdateMsg) =
                    let edge = { Source = source; Target = target; ShortChannelId = upd.ShortChannelId; Update = upd }
                    baseGraph.AddVerticesAndEdge edge |> ignore
                
                updates.Forward |> Option.iter (addEdge ann.NodeId1 ann.NodeId2)
                updates.Backward |> Option.iter (addEdge ann.NodeId2 ann.NodeId1)

            RoutingGraphData(announcements, updates, syncTimestamp, blacklistedChannels, RoutingGraph(baseGraph))

        member self.BlacklistChannel(shortChannelId: ShortChannelId) =
            let newBlacklistedChannels = blacklistedChannels |> Set.add shortChannelId
            let baseGraph = AdjacencyGraph<NodeId, RoutingGrpahEdge>()
            baseGraph.AddVerticesAndEdgeRange(
                self.Graph.Edges |> Seq.filter (fun edge ->  edge.ShortChannelId <> shortChannelId))
                |> ignore
            RoutingGraphData(announcements, updates, self.LastSyncTimestamp, newBlacklistedChannels, RoutingGraph(baseGraph))

        member self.GetChannelUpdates() =
            updates

    let mutable internal routingState = RoutingGraphData()

    /// Get gossip data either from RGS server, or from cache (if present).
    /// Only full dumps are cached. If data is received from server, cache is updated.
    /// Empty array means absence of data
    let private GetGossipData (timestamp: uint32) : Async<byte[]> =
        let isFullSync = timestamp = 0u
        let fullSyncFileInfo = FileInfo(Path.Combine(Config.GetCacheDir().FullName, "rgsFullSyncData.bin"))
        let twoWeeksAgo = DateTime.UtcNow - TimeSpan.FromDays(14.0)
        
        async { 
            if isFullSync && fullSyncFileInfo.Exists && fullSyncFileInfo.LastWriteTimeUtc > twoWeeksAgo then
                return File.ReadAllBytes fullSyncFileInfo.FullName
            elif timestamp >= uint32 ((DateTime.UtcNow - TimeSpan.FromDays(1.0)).UnixTimestamp()) then
                // RGS server is designed to take daily timestamps (00:00 of every day), so no new data is available
                return Array.empty
            else
                let url = SPrintF1 "https://rapidsync.lightningdevkit.org/snapshot/%d" timestamp
                use httpClient = new HttpClient()

                let! response = httpClient.GetAsync url |> Async.AwaitTask
                if response.StatusCode = Net.HttpStatusCode.NotFound then
                    // error 404, most likely no data available
                    // see https://github.com/lightningdevkit/rapid-gossip-sync-server/issues/16
                    return Array.empty
                else
                    let! data = response.Content.ReadAsByteArrayAsync()  |> Async.AwaitTask
                    if isFullSync then
                        File.WriteAllBytes(fullSyncFileInfo.FullName, data)
                    return data
        }

    let private SyncUsingTimestamp (timestamp: uint32) =
        async {
            let! gossipData = GetGossipData timestamp

            if Array.isEmpty gossipData then
                return ()

            use memStream = new MemoryStream(gossipData)
            use lightningReader = new LightningReaderStream(memStream)

            let prefix = Array.zeroCreate RgsPrefix.Length
            
            do! lightningReader.ReadAsync(prefix, 0, prefix.Length)
                |> Async.AwaitTask
                |> Async.Ignore

            if not (Enumerable.SequenceEqual (prefix, RgsPrefix)) then
                failwith "Invalid version prefix"

            let chainHash = lightningReader.ReadUInt256 true
            if chainHash <> Network.Main.GenesisHash then
                failwith "Invalid chain hash"

            let lastSeenTimestamp = lightningReader.ReadUInt32 false
            let secondsInWeek = uint (24 * 3600 * 7)
            let backdatedTimestamp = lastSeenTimestamp - secondsInWeek

            let nodeIdsCount = lightningReader.ReadUInt32 false
            let nodeIds = 
                Array.init
                    (int nodeIdsCount)
                    (fun _ -> lightningReader.ReadPubKey() |> NodeId)

            let announcementsCount = lightningReader.ReadUInt32 false

            let rec readAnnouncements (remainingCount: uint) (previousShortChannelId: uint64) (announcements: List<CompactAnnouncment>) =
                if remainingCount = 0u then
                    announcements
                else
                    let features = lightningReader.ReadWithLen () |> FeatureBits.TryCreate
                    let shortChannelId = previousShortChannelId + lightningReader.ReadBigSize ()
                    let nodeId1 = nodeIds.[lightningReader.ReadBigSize () |> int]
                    let nodeId2 = nodeIds.[lightningReader.ReadBigSize () |> int]

                    let compactAnn =
                        {
                            ChannelFeatures = features
                            ShortChannelId = shortChannelId |> ShortChannelId.FromUInt64
                            NodeId1 = nodeId1
                            NodeId2 = nodeId2
                        }

                    readAnnouncements (remainingCount - 1u) shortChannelId (compactAnn::announcements)

            let announcements = readAnnouncements announcementsCount 0UL List.Empty

            let updatesCount = lightningReader.ReadUInt32 false

            let defaultCltvExpiryDelta = lightningReader.ReadUInt16 false |> BlockHeightOffset16
            let defaultHtlcMinimumMSat = lightningReader.ReadUInt64 false |> LNMoney.MilliSatoshis
            let defaultFeeBaseMSat = lightningReader.ReadUInt32 false |> LNMoney.MilliSatoshis
            let defaultFeeProportionalMillionths: uint32 = lightningReader.ReadUInt32 false
            let defaultHtlcMaximumMSat = lightningReader.ReadUInt64 false |> LNMoney.MilliSatoshis

            let rec readUpdates (remainingCount: uint) (previousShortChannelId: uint64) (updates: Map<ShortChannelId, ChannelUpdates>) =
                if remainingCount = 0u then
                    updates
                else
                    let shortChannelId = previousShortChannelId + lightningReader.ReadBigSize ()
                    let customChannelFlag = lightningReader.ReadByte()
                    let standardChannelFlagMask = 0b11uy
                    let standardChannelFlag = customChannelFlag &&& standardChannelFlagMask

                    let isIncremental = 
                        customChannelFlag &&& CustomChannelUpdateFlags.IncrementalUpdate > 0uy

                    let cltvExpiryDelta =
                        if customChannelFlag &&& CustomChannelUpdateFlags.CltvExpiryDelta > 0uy then
                            lightningReader.ReadUInt16 false |> BlockHeightOffset16 |> Some
                        else
                            None

                    let htlcMinimumMSat =
                        if customChannelFlag &&& CustomChannelUpdateFlags.HtlcMinimumMsat > 0uy then
                            lightningReader.ReadUInt64 false |> LNMoney.MilliSatoshis |> Some
                        else
                            None

                    let feeBaseMSat =
                        if customChannelFlag &&& CustomChannelUpdateFlags.FeeBaseMsat > 0uy then
                            lightningReader.ReadUInt32 false |> LNMoney.MilliSatoshis |> Some
                        else
                            None

                    let feeProportionalMillionths =
                        if customChannelFlag &&& CustomChannelUpdateFlags.FeeProportionalMillionths > 0uy then
                            lightningReader.ReadUInt32 false |> Some
                        else
                            None
                    
                    let htlcMaximumMSat =
                        if customChannelFlag &&& CustomChannelUpdateFlags.HtlcMaximumMsat > 0uy then
                            lightningReader.ReadUInt64 false |> LNMoney.MilliSatoshis |> Some
                        else
                            None

                    let structuredShortChannelId = shortChannelId |> ShortChannelId.FromUInt64
                    
                    let channelUpdate =
                        let baseUpdateOption =
                            if isIncremental then
                                match updates |> Map.tryFind structuredShortChannelId with
                                | Some baseUpdates ->
                                    let isForward = (standardChannelFlag &&& 1uy) = 0uy
                                    if isForward then baseUpdates.Forward else baseUpdates.Backward
                                | None -> 
#if DEBUG
                                    Console.WriteLine(SPrintF1 "Could not find base update for channel %A" structuredShortChannelId)
#endif
                                    None
                            else
                                None

                        match baseUpdateOption with
                        | Some baseUpdate ->
                            {
                                baseUpdate with
                                    Timestamp = backdatedTimestamp
                                    CLTVExpiryDelta = cltvExpiryDelta |> Option.defaultValue baseUpdate.CLTVExpiryDelta
                                    HTLCMinimumMSat = htlcMinimumMSat |> Option.defaultValue baseUpdate.HTLCMinimumMSat
                                    FeeBaseMSat = feeBaseMSat |> Option.defaultValue baseUpdate.FeeBaseMSat
                                    FeeProportionalMillionths = 
                                        feeProportionalMillionths |> Option.defaultValue baseUpdate.FeeProportionalMillionths
                                    HTLCMaximumMSat = 
                                        match htlcMaximumMSat with
                                        | Some _ -> htlcMaximumMSat
                                        | None -> baseUpdate.HTLCMaximumMSat
                            } 
                        | None ->
                            {
                                UnsignedChannelUpdateMsg.ShortChannelId = structuredShortChannelId
                                Timestamp = backdatedTimestamp
                                ChainHash = Network.Main.GenesisHash
                                ChannelFlags = standardChannelFlag
                                MessageFlags = 1uy
                                CLTVExpiryDelta = cltvExpiryDelta |> Option.defaultValue defaultCltvExpiryDelta
                                HTLCMinimumMSat = htlcMinimumMSat |> Option.defaultValue defaultHtlcMinimumMSat
                                FeeBaseMSat = feeBaseMSat |> Option.defaultValue defaultFeeBaseMSat
                                FeeProportionalMillionths = 
                                    feeProportionalMillionths |> Option.defaultValue defaultFeeProportionalMillionths
                                HTLCMaximumMSat = 
                                    match htlcMaximumMSat with
                                    | Some _ -> htlcMaximumMSat
                                    | None -> Some defaultHtlcMaximumMSat
                            }

                    let newUpdates =
                        let oldValue =
                            match updates |> Map.tryFind structuredShortChannelId with
                            | Some(updates) -> updates
                            | None -> ChannelUpdates.Empty
                        updates |> Map.add structuredShortChannelId (oldValue.With channelUpdate)

                    readUpdates (remainingCount - 1u) shortChannelId newUpdates

            let updates = readUpdates updatesCount 0UL (routingState.GetChannelUpdates())

            routingState <- routingState.Update announcements updates lastSeenTimestamp

            return ()
        }

    let private FullSync() = SyncUsingTimestamp 0u
    let private IncrementalSync() = SyncUsingTimestamp routingState.LastSyncTimestamp

    let Sync() =
        async {
            if routingState.LastSyncTimestamp = 0u then
                // always do full sync on fresh start
                do! FullSync()
            do! IncrementalSync()
        }

    let internal BlacklistChannel (shortChannelId: ShortChannelId) =
        routingState <- routingState.BlacklistChannel shortChannelId
    
    /// Get shortest route from source to target node taking cahnnel fees and cltv expiry deltas into account.
    /// Don't use channels that have insufficient capacity for given paymentAmount.
    /// See EdgeWeightCaluculation.edgeWeight.
    /// If no routes can be found, return empty sequence.
    let internal GetRoute (sourceNodeId: NodeId) (targetNodeId: NodeId) (paymentAmount: LNMoney) : seq<RoutingGrpahEdge> =
        let tryGetPath = 
            routingState.Graph.ShortestPathsDijkstra(
                System.Func<RoutingGrpahEdge, float>(EdgeWeightCaluculation.edgeWeight paymentAmount), 
                sourceNodeId)
        match tryGetPath.Invoke targetNodeId with
        | true, path -> path
        | false, _ -> Seq.empty

    let DebugGetRoute (account: UtxoCoin.NormalUtxoAccount) (nodeAddress: string) (numSatoshis: decimal) =
        let paymentAmount = LNMoney.Satoshis numSatoshis
        let targetNodeId = NodeIdentifier.TcpEndPoint(NodeEndPoint.Parse Currency.BTC nodeAddress).NodeId
            
        let nodeIds = 
            seq {
                let channelStore = ChannelStore account
                for channelId in channelStore.ListChannelIds() do
                    let serializedChannel = channelStore.LoadChannel channelId
                    yield serializedChannel.SavedChannelState.StaticChannelConfig.RemoteNodeId
            }

        let result = 
            match nodeIds |> Seq.tryHead with
            | Some ourNodeId ->
                GetRoute ourNodeId targetNodeId paymentAmount
            | None -> Seq.empty
        
        if Seq.isEmpty result then
            Console.WriteLine("Could not find route to " + nodeAddress)
        else
            Console.WriteLine("Shortest route to " + nodeAddress)
            for edge in result do
                Console.WriteLine(SPrintF1 "%A" edge)
                Console.WriteLine(SPrintF1 "Weight: %f" (EdgeWeightCaluculation.edgeWeight paymentAmount edge))
        ignore result // Can't return result now because it depends on DNL types
