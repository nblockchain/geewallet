namespace GWallet.Backend

open System
open Nethereum.Core.Signing.Crypto

type Account =
    {
        HexPrivateKey: string;
        Currency: Currency;
    }
    member this.PublicAddress = EthECKey.GetPublicAddress(this.HexPrivateKey)
