namespace GWallet.Backend.Ether

open System
open System.Numerics

open GWallet.Backend

open Nethereum.Web3
open Nethereum.Util

type MinerFee(gasPriceInWei: Int64, estimationTime: DateTime, currency: Currency) =
    member val GasPriceInWei = gasPriceInWei with get
    member val Currency = currency with get

    static member internal GAS_COST_FOR_A_NORMAL_ETHER_TRANSACTION = BigInteger(21000)

    // FIXME: how to not repeat properties but still have them serialized
    // as part of the public interface?? :(
    member val EstimationTime = estimationTime with get

    interface IBlockchainFee with
        member val EstimationTime = estimationTime with get

        member val Value =
            let gasPriceInWei = BigInteger(gasPriceInWei)
            let costInWei = BigInteger.Multiply(gasPriceInWei, MinerFee.GAS_COST_FOR_A_NORMAL_ETHER_TRANSACTION)
            UnitConversion.Convert.FromWei(costInWei, UnitConversion.EthUnit.Ether) with get
