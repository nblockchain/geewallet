namespace GWallet.Backend.Ether

open System
open System.Numerics

open GWallet.Backend

open Nethereum.Util

type MinerFee(gasLimit: Int64, gasPriceInWei: Int64, estimationTime: DateTime, currency: Currency) =

    member val GasLimit = gasLimit with get
    member val GasPriceInWei = gasPriceInWei with get
    member val Currency = currency with get
    member val EstimationTime = estimationTime with get

    member self.CalculateAbsoluteValue() =
        let gasPriceInWei = BigInteger(gasPriceInWei)
        let costInWei = BigInteger.Multiply(gasPriceInWei, BigInteger(gasLimit))
        UnitConversion.Convert.FromWei(costInWei, UnitConversion.EthUnit.Ether)
