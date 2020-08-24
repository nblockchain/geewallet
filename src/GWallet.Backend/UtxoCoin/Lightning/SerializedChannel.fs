namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Net

open Newtonsoft.Json
open NBitcoin
open DotNetLightning.Channel
open DotNetLightning.Utils
open DotNetLightning.Crypto
open DotNetLightning.Transactions

open GWallet.Backend.FSharpUtil

type SerializedChannel =
    {
        ChannelIndex: int
        Network: Network
        ChanState: ChannelState
        AccountFileName: string
        // FIXME: should store just RemoteNodeEndPoint instead of CounterpartyIP+RemoteNodeId?
        CounterpartyIP: IPEndPoint
        RemoteNodeId: NodeId
        // this is the amount of confirmations that the counterparty told us that the funding transaction needs
        MinSafeDepth: BlockHeightOffset32
    }
    static member LightningSerializerSettings: JsonSerializerSettings =
        let settings = JsonMarshalling.SerializerSettings
        settings


module ChannelSerialization =
    let internal Commitments (serializedChannel: SerializedChannel): Commitments =
        UnwrapOption
            serializedChannel.ChanState.Commitments
            "A SerializedChannel is only created once a channel has started \
            being established and must therefore have an initial commitment"

    let IsFunder (serializedChannel: SerializedChannel): bool =
        (Commitments serializedChannel).LocalParams.IsFunder

    let internal Capacity (serializedChannel: SerializedChannel): Money =
        (Commitments serializedChannel).FundingScriptCoin.Amount

    let internal Balance (serializedChannel: SerializedChannel): DotNetLightning.Utils.LNMoney =
        (Commitments serializedChannel).LocalCommit.Spec.ToLocal

    let internal SpendableBalance (serializedChannel: SerializedChannel): DotNetLightning.Utils.LNMoney =
        (Commitments serializedChannel).SpendableBalance()

    // How low the balance can go. A channel must maintain enough balance to
    // cover the channel reserve. The funder must also keep enough in the
    // channel to cover the closing fee.
    let internal MinBalance (serializedChannel: SerializedChannel): DotNetLightning.Utils.LNMoney =
        (Balance serializedChannel) - (SpendableBalance serializedChannel)

    // How high the balance can go. The fundee will only be able to receive up
    // to this amount before the funder no longer has enough funds to cover
    // the channel reserve and closing fee.
    let internal MaxBalance (serializedChannel: SerializedChannel): DotNetLightning.Utils.LNMoney =
        let capacity = LNMoney.FromMoney <| (Capacity serializedChannel)
        let channelReserve =
            LNMoney.FromMoney (Commitments serializedChannel).LocalParams.ChannelReserveSatoshis
        let fee =
            if (IsFunder serializedChannel) then
                let feeRate = (Commitments serializedChannel).LocalCommit.Spec.FeeRatePerKw
                let weight = COMMITMENT_TX_BASE_WEIGHT
                LNMoney.FromMoney <| feeRate.CalculateFeeFromWeight weight
            else
                LNMoney.Zero
        capacity - channelReserve - fee

    let ChannelId (serializedChannel: SerializedChannel): ChannelIdentifier =
        ChannelIdentifier.FromDnl (Commitments serializedChannel).ChannelId

