namespace GWallet.Backend

open System
open NBitcoin.Crypto
open Nethereum.Core.Signing.Crypto
open Nethereum.KeyStore

type IAccount =
    abstract member Currency: Currency with get
    abstract member PublicAddress: string with get

type NormalAccount(currency: Currency, json: string) =
    member val Json = json with get

    static member internal KeyStoreService: KeyStoreService = KeyStoreService()

    interface IAccount with
        member val Currency = currency with get
        member val PublicAddress =
            sprintf "0x%s" (NormalAccount.KeyStoreService.GetAddressFromKeyStore(json)) with get

type ReadOnlyAccount(currency: Currency, publicAddress: string) =
    interface IAccount with
        member val Currency = currency with get
        member val PublicAddress = publicAddress with get
