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
        File.ReadAllText accountFile.FullName

    interface IAccount with
        member val Currency = currency with get
        member val PublicAddress =
            fromAccountFileToPublicAddress accountFile with get

type ReadOnlyAccount(currency: Currency, publicAddress: string) =
    interface IAccount with
        member val Currency = currency with get
        member val PublicAddress = publicAddress with get

type ArchivedAccount(currency: Currency, accountFile: FileInfo, fromUnencryptedPrivateKeyToPublicAddress: string -> string) =
    member internal __.GetUnencryptedPrivateKey() =
        File.ReadAllText accountFile.FullName

    interface IAccount with
        member val Currency = currency with get
        member self.PublicAddress
            with get() =
                fromUnencryptedPrivateKeyToPublicAddress (self.GetUnencryptedPrivateKey())
