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
        KeyStoreService().GetAddressFromKeyStore(this.Json)
