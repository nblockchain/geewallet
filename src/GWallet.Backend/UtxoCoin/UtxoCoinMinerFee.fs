namespace GWallet.Backend.UtxoCoin

open System

open NBitcoin

open GWallet.Backend

//FIXME: convert to record?
type MinerFee(estimatedFeeInSatoshis: int64,
              estimationTime: DateTime,
              currency: Currency) =

    member val EstimatedFeeInSatoshis = estimatedFeeInSatoshis with get

    member val EstimationTime = estimationTime with get

    member val Currency = currency with get

    member __.CalculateAbsoluteValue() =
        let money = NBitcoin.Money.Satoshis estimatedFeeInSatoshis
        money.ToUnit MoneyUnit.BTC

    // FIXME: we should share some code between this method and EtherMinerFee's
    static member GetHigherFeeThanRidiculousFee
        (exchangeRateToFiat: decimal)

        //public nodes as in the equivalent ones to Electrum Servers
        (initialFeeWithAMinimumGasPriceInWeiDictatedByPublicNodes: MinerFee)
        =
        let initialAbsoluteValue =
            initialFeeWithAMinimumGasPriceInWeiDictatedByPublicNodes.CalculateAbsoluteValue()
        if initialAbsoluteValue * exchangeRateToFiat >=
           FiatValueEstimation.SmallestFiatFeeThatIsNoLongerRidiculous then
            initialFeeWithAMinimumGasPriceInWeiDictatedByPublicNodes
        else
            let biggerFee =
                NBitcoin.Money(
                    FiatValueEstimation.SmallestFiatFeeThatIsNoLongerRidiculous
                        / exchangeRateToFiat,
                    NBitcoin.MoneyUnit.BTC
                )
            let estimationTime =
                initialFeeWithAMinimumGasPriceInWeiDictatedByPublicNodes.EstimationTime
            let currency =
                initialFeeWithAMinimumGasPriceInWeiDictatedByPublicNodes.Currency
            MinerFee(biggerFee.Satoshi, estimationTime, currency)

    interface IBlockchainFeeInfo with
        member self.FeeEstimationTime = self.EstimationTime
        member self.FeeValue = self.CalculateAbsoluteValue()
        member self.Currency = self.Currency
