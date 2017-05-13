namespace GWallet.Backend

open System
open NBitcoin.Crypto
open Nethereum.Core.Signing.Crypto
open Nethereum.KeyStore

type Account =
    {
        Json: string;
        Currency: Currency;
    }
    member this.PublicAddress: string =
        "0x" + KeyStoreService().GetAddressFromKeyStore(this.Json)
