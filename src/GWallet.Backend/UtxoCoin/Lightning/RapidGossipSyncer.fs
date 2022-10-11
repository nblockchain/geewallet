namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.IO
open System.Net.Http
open System.Linq

open NBitcoin
open DotNetLightning.Serialization
open DotNetLightning.Serialization.Msgs
open DotNetLightning.Utils
open DotNetLightning.Routing

open ResultUtils.Portability
open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks


module RapidGossipSyncer =
    
    let private RgsPrefix = [| 76uy; 68uy; 75uy; 1uy |]
    
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
    

    let mutable private routingState = RoutingGraphData()

    // functions for testing
    let GetLastSyncTimestamp() = routingState.LastSyncTimestamp
    let GetGraphEdgeCount() = routingState.Graph.EdgeCount

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

    let SyncUsingData (gossipData: byte[]) =
        async {
            if Array.isEmpty gossipData then
                return ()
            else
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

                let rec readAnnouncements (remainingCount: uint) 
                                          (previousShortChannelId: uint64) 
                                          (channelDescriptions: List<ChannelDesc>) =
                    if remainingCount = 0u then
                        channelDescriptions
                    else
                        let _features = lightningReader.ReadWithLen ()
                        let shortChannelId = previousShortChannelId + lightningReader.ReadBigSize ()
                        let nodeId1 = nodeIds.[lightningReader.ReadBigSize () |> int]
                        let nodeId2 = nodeIds.[lightningReader.ReadBigSize () |> int]

                        let desc =
                            {
                                ShortChannelId = shortChannelId |> ShortChannelId.FromUInt64
                                A = nodeId1
                                B = nodeId2
                            }

                        readAnnouncements (remainingCount - 1u) shortChannelId (desc::channelDescriptions)

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
                                        Infrastructure.LogDebug
                                            <| SPrintF1 "Could not find base update for channel %A" structuredShortChannelId
                                        
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
        }

    let private FullSync() = 
        async { 
            let! data = GetGossipData 0u
            return! SyncUsingData data
        }

    let private IncrementalSync() = 
        async { 
            let! data = GetGossipData routingState.LastSyncTimestamp
            return! SyncUsingData data
        }

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
    /// See RoutingGraphData.GetRoute
    let internal GetRoute 
        (sourceNodeId: NodeId) 
        (targetNodeId: NodeId) 
        (paymentAmount: LNMoney) 
        (currentBlockHeight: uint32)
        (routeParams: RouteParams)
        (extraHops: DotNetLightning.Payment.ExtraHop list list) 
        : seq<IRoutingHopInfo> =
        routingState.GetRoute sourceNodeId targetNodeId paymentAmount currentBlockHeight routeParams extraHops
