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

type NormalAccount(currency: Currency, accountFile: FileInfo, fromAccountFileToPublicAddress: FileInfo -> string) =
    member val AccountFile = accountFile with get

    member internal self.GetEncryptedPrivateKey() =
        fromAccountFileToPublicAddress accountFile

    interface IAccount with
        member val Currency = currency with get
        member val PublicAddress =
            fromAccountFileToPublicAddress accountFile with get

type ReadOnlyAccount(currency: Currency, accountFile: FileInfo, fromAccountFileToPublicAddress: FileInfo -> string) =
    interface IAccount with
        member val Currency = currency with get
        member self.PublicAddress
            with get() =
                fromAccountFileToPublicAddress accountFile

type ArchivedAccount(currency: Currency, accountFile: FileInfo, fromAccountFileToPublicAddress: FileInfo -> string) =
    member internal __.GetUnencryptedPrivateKey() =
        fromAccountFileToPublicAddress accountFile

    interface IAccount with
        member val Currency = currency with get
        member self.PublicAddress
            with get() =
                fromAccountFileToPublicAddress accountFile
