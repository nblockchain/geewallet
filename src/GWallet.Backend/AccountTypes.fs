namespace GWallet.Backend

open System.IO

type ConceptAccount =
    {
        Currency: Currency;
        FileNameAndContent: string*string;
        ExtractPublicAddressFromConfigFileFunc: FileInfo->string;
    }

type IAccount =
    abstract member Currency: Currency with get
    abstract member PublicAddress: string with get

type BaseAccount(currency: Currency, accountFile: FileInfo, fromAccountFileToPublicAddress: FileInfo -> string) =
    member val AccountFile = accountFile with get

    interface IAccount with
        member val Currency = currency with get
        member val PublicAddress =
            fromAccountFileToPublicAddress accountFile with get

type NormalAccount(currency: Currency, accountFile: FileInfo, fromAccountFileToPublicAddress: FileInfo -> string) =
    inherit BaseAccount(currency, accountFile, fromAccountFileToPublicAddress)

    member internal self.GetEncryptedPrivateKey() =
        fromAccountFileToPublicAddress accountFile

type ReadOnlyAccount(currency: Currency, accountFile: FileInfo, fromAccountFileToPublicAddress: FileInfo -> string) =
    inherit BaseAccount(currency, accountFile, fromAccountFileToPublicAddress)

type ArchivedAccount(currency: Currency, accountFile: FileInfo, fromAccountFileToPublicAddress: FileInfo -> string) =
    inherit BaseAccount(currency, accountFile, fromAccountFileToPublicAddress)

    member internal __.GetUnencryptedPrivateKey() =
        fromAccountFileToPublicAddress accountFile
