namespace GWallet.Backend

open System
open System.Numerics

open Nethereum.Web3

type EtherMinerFee =
    {
        GasPriceInWei: BigInteger;
        EstimationTime: DateTime;
        Currency: Currency;
    }
    member internal this.GAS_COST_FOR_A_NORMAL_ETHER_TRANSACTION = BigInteger(21000)
    member this.EtherPriceForNormalTransaction: decimal =
        let costInWei = BigInteger.Multiply(this.GasPriceInWei, this.GAS_COST_FOR_A_NORMAL_ETHER_TRANSACTION)
        UnitConversion.Convert.FromWei(costInWei, UnitConversion.EthUnit.Ether)

