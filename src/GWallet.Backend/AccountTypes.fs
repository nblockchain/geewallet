namespace GWallet.Backend

open System.IO

open Nethereum.KeyStore
open Nethereum.Signer

type IAccount =
    abstract member Currency: Currency with get
    abstract member PublicAddress: string with get

type NormalAccount(currency: Currency, jsonStoreFile: FileInfo) =
    member val Json = File.ReadAllText(jsonStoreFile.FullName) with get
    member val JsonStoreFile = jsonStoreFile with get

    static member internal KeyStoreService: KeyStoreService = KeyStoreService()
    static member internal GetPublicAddressFromKeyStore(jsonContent: string) =
        NormalAccount.KeyStoreService.GetAddressFromKeyStore(jsonContent)

    interface IAccount with
        member val Currency = currency with get
        member val PublicAddress =
            let jsonContent = File.ReadAllText(jsonStoreFile.FullName)
            NormalAccount.GetPublicAddressFromKeyStore(jsonContent) with get

type ReadOnlyAccount(currency: Currency, publicAddress: string) =
    interface IAccount with
        member val Currency = currency with get
        member val PublicAddress = publicAddress with get

type ArchivedAccount(currency: Currency, privateKey: string) =
    member val internal PrivateKey = privateKey with get

    interface IAccount with
        member val Currency = currency with get
        member val PublicAddress =
            // FIXME: ideally the ctor should receive the EthECKey to have
            //        validation of the private key ahead of time, instead
            //        of lazily when we want to retreive the public address
            EthECKey.GetPublicAddress(privateKey) with get
