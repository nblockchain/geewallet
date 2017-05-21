namespace GWallet.Backend

open System
open System.Numerics

open Nethereum.Web3

type EtherMinerFee =
    {
        GasPriceInWei: Int64;
        EstimationTime: DateTime;
        Currency: Currency;
    }
    member internal this.GAS_COST_FOR_A_NORMAL_ETHER_TRANSACTION = BigInteger(21000)
    member this.EtherPriceForNormalTransaction: decimal =
        let gasPriceInWei = BigInteger(this.GasPriceInWei)
        let costInWei = BigInteger.Multiply(gasPriceInWei, this.GAS_COST_FOR_A_NORMAL_ETHER_TRANSACTION)
        UnitConversion.Convert.FromWei(costInWei, UnitConversion.EthUnit.Ether)

