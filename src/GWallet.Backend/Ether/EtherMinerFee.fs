namespace GWallet.Backend.Ether

open System
open System.Numerics

open GWallet.Backend

open Nethereum.Util

type MinerFee (gasLimit: Int64, gasPriceInWei: Int64, estimationTime: DateTime, currency: Currency) =

    member val GasLimit = gasLimit
    member val GasPriceInWei = gasPriceInWei
    member val Currency = currency
    member val EstimationTime = estimationTime

    member __.CalculateAbsoluteValue () =
        let gasPriceInWei = BigInteger (gasPriceInWei)
        let costInWei = BigInteger.Multiply (gasPriceInWei, BigInteger (gasLimit))
        UnitConversion.Convert.FromWei (costInWei, UnitConversion.EthUnit.Ether)
