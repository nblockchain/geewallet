namespace GWallet.Backend.UtxoCoin.Lightning

open System.IO
open System.Net.Http
open System.Linq

open NBitcoin
open DotNetLightning.Serialization
open DotNetLightning.Serialization.Msgs
open DotNetLightning.Utils
open ResultUtils.Portability
open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks
open QuikGraph
open QuikGraph.Algorithms


type internal RoutingGrpahEdge = 
    {
        Source : NodeId
        Target : NodeId
        ShortChannelId : ShortChannelId
        Update: UnsignedChannelUpdateMsg
    }
    with
        interface IEdge<NodeId> with
            member this.Source = this.Source
            member this.Target = this.Target

type internal RoutingGraph = ArrayAdjacencyGraph<NodeId, RoutingGrpahEdge>


module private RoutingHeuristics =
    // code moslty from DotNetLightning
    let BLOCK_TIME_TWO_MONTHS = 8640us |> BlockHeightOffset16
    let CAPACITY_CHANNEL_LOW = LNMoney.Satoshis(1000L)

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
    let CLTVDeltaFactor = 1.0
    let CapacityFactor = 1.0


module private EdgeWeightCaluculation =
    // code is partly from DotNetLightning

    let nodeFee (baseFee: LNMoney) (proportionalFee: int64) (paymentAmount: LNMoney) =
        baseFee + LNMoney.Satoshis(decimal(paymentAmount.Satoshi * proportionalFee) / 1000000.0m)
        
    let edgeFeeCost (amountWithFees: LNMoney) (edge: RoutingGrpahEdge) =
        let { Update = update } = edge
        let result =
            nodeFee
                update.FeeBaseMSat 
                (int64 update.FeeProportionalMillionths)
                amountWithFees
        // We can't have zero fee cost because it causes weight to be 0 regardless of expiry_delta
        LNMoney.Max(result, LNMoney.MilliSatoshis(1))

    /// Computes the weight for the given edge
    let edgeWeight (paymentAmount: LNMoney) (edge: RoutingGrpahEdge) : float =
        let feeCost = float (edgeFeeCost paymentAmount edge).Value
        let channelCLTVDelta = edge.Update.CLTVExpiryDelta
        let edgeMaxCapacity =
            edge.Update.HTLCMaximumMSat
            |> Option.defaultValue(RoutingHeuristics.CAPACITY_CHANNEL_LOW)
        if edgeMaxCapacity < paymentAmount then
            infinity // chanel capacity is too small, reject edge
        else
            let capFactor =
                1.0 - RoutingHeuristics.normalize(
                        float edgeMaxCapacity.MilliSatoshi,
                        float RoutingHeuristics.CAPACITY_CHANNEL_LOW.MilliSatoshi,
                        float RoutingHeuristics.CAPACITY_CHANNEL_HIGH.MilliSatoshi)
            let cltvFactor =
                RoutingHeuristics.normalize(
                    float channelCLTVDelta.Value,
                    float RoutingHeuristics.CLTV_LOW,
                    float RoutingHeuristics.CLTV_HIGH)
            let factor = 
                cltvFactor * RoutingHeuristics.CLTVDeltaFactor 
                + capFactor * RoutingHeuristics.CapacityFactor
            factor * feeCost


module RapidGossipSyncer =
    
    let private RGSPrefix = [| 76uy; 68uy; 75uy; 1uy |]

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
        with
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
    
    /// Class responsible for storing and updating routing graph
    type internal RoutingState() =
        let announcements = System.Collections.Generic.HashSet<CompactAnnouncment>()
        let mutable updates: Map<ShortChannelId, ChannelUpdates> = Map.empty
        let mutable lastSyncTimestamp = 0u
        let mutable routingGraph: RoutingGraph = RoutingGraph(AdjacencyGraph())

        member self.LastSyncTimestamp = lastSyncTimestamp

        member self.Graph = routingGraph

        member self.Update (newAnnouncements : seq<CompactAnnouncment>) 
                           (newUpdates: Map<ShortChannelId, ChannelUpdates>) 
                           (syncTimestamp: uint32) =
            announcements.UnionWith newAnnouncements
            
            if updates.IsEmpty then
                updates <- newUpdates
            else
                newUpdates |> Map.iter (fun channelId newUpd ->
                    match updates |> Map.tryFind channelId with
                    | Some(upd) ->
                        updates <- updates |> Map.add channelId (upd.Combine newUpd)
                    | None ->
                        updates <- updates |> Map.add channelId newUpd )

            let baseGraph = AdjacencyGraph<NodeId, RoutingGrpahEdge>()

            for ann in announcements do
                let updates = updates.[ann.ShortChannelId]
                
                let addEdge source traget (upd : UnsignedChannelUpdateMsg) =
                    let edge = { Source=source; Target=traget; ShortChannelId=upd.ShortChannelId; Update=upd }
                    baseGraph.AddVerticesAndEdge edge |> ignore
                
                updates.Forward |> Option.iter (addEdge ann.NodeId1 ann.NodeId2)
                updates.Backward |> Option.iter (addEdge ann.NodeId2 ann.NodeId1)

            routingGraph <- RoutingGraph(baseGraph)
            lastSyncTimestamp <- syncTimestamp

    let internal routingState = RoutingState()

    let Sync () =
        async {
            use httpClient = new HttpClient()

            let! gossipData =
                let url = SPrintF1 "https://rapidsync.lightningdevkit.org/snapshot/%d" routingState.LastSyncTimestamp
                httpClient.GetByteArrayAsync url
                |> Async.AwaitTask

            use memStream = new MemoryStream(gossipData)
            use lightningReader = new LightningReaderStream(memStream)

            let prefix = Array.zeroCreate RGSPrefix.Length
            
            do! lightningReader.ReadAsync(prefix, 0, prefix.Length)
                |> Async.AwaitTask
                |> Async.Ignore

            if not (Enumerable.SequenceEqual (prefix, RGSPrefix)) then
                failwith "Invalid version prefix"

            let chainHash = lightningReader.ReadUInt256 true
            if chainHash <> Network.Main.GenesisHash then
                failwith "Invalid chain hash"

            let lastSeenTimestamp = lightningReader.ReadUInt32 false
            let backdatedTimestamp = lastSeenTimestamp - uint (24 * 3600 * 7)

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

            let defaultCltvExpiryDelta: uint16 = lightningReader.ReadUInt16 false
            let defaultHtlcMinimumMSat: uint64 = lightningReader.ReadUInt64 false
            let defaultFeeBaseMSat: uint32 = lightningReader.ReadUInt32 false
            let defaultFeeProportionalMillionths: uint32 = lightningReader.ReadUInt32 false
            let defaultHtlcMaximumMSat: uint64 = lightningReader.ReadUInt64 false

            let rec readUpdates (remainingCount: uint) (previousShortChannelId: uint64) (updates: Map<ShortChannelId, ChannelUpdates>) =
                if remainingCount = 0u then
                    updates
                else
                    let shortChannelId = previousShortChannelId + lightningReader.ReadBigSize ()
                    let customChannelFlag = lightningReader.ReadByte()
                    let standardChannelFlagMask = 0b11uy
                    let standardChannelFlag = customChannelFlag &&& standardChannelFlagMask

                    if customChannelFlag &&& CustomChannelUpdateFlags.IncrementalUpdate > 0uy then
                        failwith "We don't support increamental updates yet!"        

                    let cltvExpiryDelta =
                        if customChannelFlag &&& CustomChannelUpdateFlags.CltvExpiryDelta > 0uy then
                            lightningReader.ReadUInt16 false
                        else
                            defaultCltvExpiryDelta

                    let htlcMinimumMSat =
                        if customChannelFlag &&& CustomChannelUpdateFlags.HtlcMinimumMsat > 0uy then
                            lightningReader.ReadUInt64 false
                        else
                            defaultHtlcMinimumMSat

                    let feeBaseMSat =
                        if customChannelFlag &&& CustomChannelUpdateFlags.FeeBaseMsat > 0uy then
                            lightningReader.ReadUInt32 false
                        else
                            defaultFeeBaseMSat

                    let feeProportionalMillionths =
                        if customChannelFlag &&& CustomChannelUpdateFlags.FeeProportionalMillionths > 0uy then
                            lightningReader.ReadUInt32 false
                        else
                            defaultFeeProportionalMillionths
                    
                    let htlcMaximumMSat =
                        if customChannelFlag &&& CustomChannelUpdateFlags.HtlcMaximumMsat > 0uy then
                            lightningReader.ReadUInt64 false
                        else
                            defaultHtlcMaximumMSat

                    let structuredShortChannelId = shortChannelId |> ShortChannelId.FromUInt64

                    let channelUpdate =
                        {
                            UnsignedChannelUpdateMsg.ShortChannelId = structuredShortChannelId
                            Timestamp = backdatedTimestamp
                            ChainHash = Network.Main.GenesisHash
                            ChannelFlags = standardChannelFlag
                            MessageFlags = 1uy
                            CLTVExpiryDelta = cltvExpiryDelta |> BlockHeightOffset16
                            HTLCMinimumMSat = htlcMinimumMSat |> LNMoney.MilliSatoshis
                            FeeBaseMSat = feeBaseMSat |> LNMoney.MilliSatoshis
                            FeeProportionalMillionths = feeProportionalMillionths 
                            HTLCMaximumMSat = htlcMaximumMSat |> LNMoney.MilliSatoshis |> Some
                        }

                    let newUpdates =
                        let oldValue =
                            match updates |> Map.tryFind structuredShortChannelId with
                            | Some(updates) -> updates
                            | None -> ChannelUpdates.Empty
                        updates |> Map.add structuredShortChannelId (oldValue.With channelUpdate)

                    readUpdates (remainingCount - 1u) shortChannelId newUpdates

            let updates = readUpdates updatesCount 0UL Map.empty

            routingState.Update announcements updates lastSeenTimestamp

            return ()
        }
    
    let GetRoute (account: UtxoCoin.NormalUtxoAccount) (nodeAddress: string) (numSatoshis: decimal) =
        let targetNodeId = NodeIdentifier.TcpEndPoint(NodeEndPoint.Parse Currency.BTC nodeAddress).NodeId
        
        let nodeIds = 
            seq {
                let channelStore = ChannelStore account
                for channelId in channelStore.ListChannelIds() do
                    let serializedChannel = channelStore.LoadChannel channelId
                    yield serializedChannel.SavedChannelState.StaticChannelConfig.RemoteNodeId
            }
        
        let paymentAmount = LNMoney.Satoshis(numSatoshis)

        let result = 
            match nodeIds |> Seq.tryHead with
            | Some(ourNodeId) ->
                let tryGetPath = 
                    routingState.Graph.ShortestPathsDijkstra(
                        System.Func<RoutingGrpahEdge, float>(EdgeWeightCaluculation.edgeWeight paymentAmount), 
                        ourNodeId)
                match tryGetPath.Invoke targetNodeId with
                | true, path -> path
                | false, _ -> Seq.empty      
            | None -> Seq.empty
        
        if Seq.isEmpty result then
            System.Console.WriteLine("Could not find route to " + nodeAddress)
        else
            System.Console.WriteLine("Shortest route to " + nodeAddress)
            for edge in result do
                System.Console.WriteLine(SPrintF1 "%A" edge)
                System.Console.WriteLine(SPrintF1 "Weight: %f" (EdgeWeightCaluculation.edgeWeight paymentAmount edge))
        ignore result // Can't return result now because it depends on DNL types