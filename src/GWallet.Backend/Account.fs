namespace GWallet.Backend

open System
open Nethereum.Core.Signing.Crypto

type Account =
    {
        Id: Guid;
        HexPrivateKey: string;
        Currency: Currency;
    }
    member this.PublicAddress = EthECKey.GetPublicAddress(this.HexPrivateKey)
