namespace GWallet.Backend.UtxoCoin.Lightning

open System

open NBitcoin
open DotNetLightning.Utils
open DotNetLightning.Serialization
open DotNetLightning.Chain
open DotNetLightning.Channel

open GWallet.Backend

module Settings =

    let Currencies = [| Currency.BTC; Currency.LTC |] :> seq<Currency>

    let internal ConfigDirName = "LN"

    let private ToSelfDelay currency =
        match currency with
        | BTC -> 2016us
        | LTC -> 8064us
        | _ -> failwith "Unsupported currency"
        |> BlockHeightOffset16

    let internal PeerLimits (currency: Currency) : ChannelHandshakeLimits = {
        ForceChannelAnnouncementPreference = false
        MinFundingSatoshis = Money 100L
        MaxHTLCMinimumMSat = LNMoney 100000L
        MinMaxHTLCValueInFlightMSat = LNMoney 10000L
        MaxChannelReserveSatoshis = Money 100000L
        MinMaxAcceptedHTLCs = 1us
        MinDustLimitSatoshis = Money 200L // Value used by lnd
        MaxDustLimitSatoshis = Money 10000000L
        // TODO make optional in DotNetLightning
        MaxMinimumDepth = BlockHeightOffset32 UInt32.MaxValue
        MaxToSelfDelay = ToSelfDelay currency
    }

    let private SupportedFeatures (funding: Money) (currency: Currency) =
        let featureBits = FeatureBits.Zero
        featureBits.SetFeature Feature.OptionDataLossProtect FeaturesSupport.Optional true
        if currency = Currency.LTC then
            let featureType =
                if funding > ChannelConstants.MAX_FUNDING_SATOSHIS then
                    FeaturesSupport.Mandatory
                else
                    FeaturesSupport.Optional

            featureBits.SetFeature Feature.OptionSupportLargeChannel featureType true
        featureBits

    let internal GetLocalParams (funding: Money)
                                (currency: Currency)
                                    : LocalParams =
        {
            DustLimitSatoshis = Money 200UL
            MaxHTLCValueInFlightMSat = LNMoney 10000L
            ChannelReserveSatoshis = funding * Config.ChannelReservePercentage / 100L
            HTLCMinimumMSat = LNMoney 1000L
            // see https://github.com/lightning/bolts/blob/master/02-peer-protocol.md#the-open_channel-message
            ToSelfDelay = ToSelfDelay currency
            MaxAcceptedHTLCs = uint16 10
            Features = SupportedFeatures funding currency
        }
