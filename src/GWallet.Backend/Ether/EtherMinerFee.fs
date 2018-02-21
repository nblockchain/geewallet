namespace GWallet.Backend.Ether

open System
open System.Numerics

open GWallet.Backend

open Nethereum.Util

type MinerFee(gasPriceInWei: Int64, estimationTime: DateTime, currency: Currency) =
    member val GasPriceInWei = gasPriceInWei with get
    member val Currency = currency with get

    static member internal GAS_COST_FOR_A_NORMAL_ETHER_TRANSACTION = BigInteger(21000)

    member val EstimationTime = estimationTime with get

    member this.CalculateAbsoluteValue() =
        let gasPriceInWei = BigInteger(gasPriceInWei)
        let costInWei = BigInteger.Multiply(gasPriceInWei, MinerFee.GAS_COST_FOR_A_NORMAL_ETHER_TRANSACTION)
        UnitConversion.Convert.FromWei(costInWei, UnitConversion.EthUnit.Ether)
