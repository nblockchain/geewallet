namespace GWallet.Backend

open System.IO

open Nethereum.KeyStore
open Nethereum.Signer

type IAccount =
    abstract member Currency: Currency with get
    abstract member PublicAddress: string with get

type NormalAccount(currency: Currency, accountFile: FileInfo, fromPrivKeyToPublicAddress: FileInfo -> string) =
    member val AccountFile = accountFile with get

    interface IAccount with
        member val Currency = currency with get
        member val PublicAddress =
            fromPrivKeyToPublicAddress accountFile with get

type ReadOnlyAccount(currency: Currency, publicAddress: string) =
    interface IAccount with
        member val Currency = currency with get
        member val PublicAddress = publicAddress with get

type ArchivedAccount(currency: Currency, privateKey: EthECKey) =
    member val internal PrivateKey = privateKey with get

    interface IAccount with
        member val Currency = currency with get
        member val PublicAddress =
            privateKey.GetPublicAddress() with get
