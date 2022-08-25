namespace GWallet.Backend.UtxoCoin.Lightning

open System

open NBitcoin
open DotNetLightning.Utils
open DotNetLightning.Serialization
open DotNetLightning.Serialization.Msgs
open DotNetLightning.Chain
open DotNetLightning.Channel

open GWallet.Backend


type ConnectionPurpose =
    | ChannelOpening
    | Routing


module Settings =

    let Currencies = [| Currency.BTC; Currency.LTC |] :> seq<Currency>

    let internal ConfigDirName = "LN"

    let private MaxToSelfDelay currency =
        match currency with
        | BTC -> 2016us
        | LTC -> 8064us
        | _ -> failwith "Unsupported currency"
        |> BlockHeightOffset16

    let private ToSelfDelay currency =
#if DEBUG
        2us
        |> BlockHeightOffset16
#else
        MaxToSelfDelay currency
#endif

    let internal DefaultTxMinimumDepth (currency: Currency) =
        match currency with
        | BTC -> 2u
        | LTC -> 3u
        | _ -> failwith "Unsupported currency"
        |> BlockHeightOffset32

    let internal PeerLimits (funding: Money) (currency: Currency) : ChannelHandshakeLimits = {
        ForceChannelAnnouncementPreference = false
        MinFundingSatoshis = Money 100L
        MaxHTLCMinimumMSat = LNMoney 100000L
        MinMaxHTLCValueInFlightMSat = LNMoney 10000L
        // Fail if we consider the channel reserve to be too large.  We
        // currently fail if it is greater than 20% of the channel capacity.
        MaxChannelReserveSatoshis = funding / 5L
        MinMaxAcceptedHTLCs = 1us
        MinDustLimitSatoshis = Money 200L // Value used by lnd
        MaxDustLimitSatoshis = Money 10000000L
        // TODO make optional in DotNetLightning
        MaxMinimumDepth = BlockHeightOffset32 UInt32.MaxValue
        MaxToSelfDelay = MaxToSelfDelay currency
    }

    let internal SupportedFeatures (currency: Currency) (fundingOpt: Option<Money>) (purpose: ConnectionPurpose) =
        let featureBits =
            ((FeatureBits.Zero.SetFeature Feature.OptionDataLossProtect FeaturesSupport.Optional true)
                .SetFeature Feature.VariableLengthOnion FeaturesSupport.Mandatory true)
                .SetFeature Feature.OptionStaticRemoteKey FeaturesSupport.Mandatory true      
        let featureBits =
            match purpose with
            | Routing -> 
                featureBits.SetFeature Feature.ChannelRangeQueries FeaturesSupport.Mandatory true
            | ChannelOpening -> 
                featureBits.SetFeature Feature.OptionAnchorZeroFeeHtlcTx FeaturesSupport.Mandatory true

        if currency = Currency.LTC then
            let featureType =
                match fundingOpt with
                | Some funding when funding > ChannelConstants.MAX_FUNDING_SATOSHIS ->
                    FeaturesSupport.Mandatory
                | _ ->
                    FeaturesSupport.Optional

            featureBits.SetFeature Feature.OptionSupportLargeChannel featureType true
        else
            featureBits

    let internal GetLocalParams (funding: Money)
                                (currency: Currency)
                                    : LocalParams =
        {
            DustLimitSatoshis =
                match currency with
                | BTC -> 354UL
                | LTC -> 200UL
                | _ -> failwith "Unsupported currency"
                |> Money
            MaxHTLCValueInFlightMSat = LNMoney.FromMoney funding
            ChannelReserveSatoshis = funding * Config.ChannelReservePercentage / 100L
            HTLCMinimumMSat = LNMoney 1000L
            // see https://github.com/lightning/bolts/blob/master/02-peer-protocol.md#the-open_channel-message
            ToSelfDelay = ToSelfDelay currency
            MaxAcceptedHTLCs = uint16 10
            Features = SupportedFeatures currency (Some funding) ChannelOpening
        }
