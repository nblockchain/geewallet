namespace GWallet.Backend

type FileRepresentation =
    {
        Name: string;
        Content: unit->string;
    }

type ConceptAccount =
    {
        Currency: Currency;
        FileRepresentation: FileRepresentation;
        ExtractPublicAddressFromConfigFileFunc: FileRepresentation->string;
    }

type AccountKind =
    | Normal
    | ReadOnly
    | Archived

type IAccount =
    abstract member Currency: Currency with get
    abstract member PublicAddress: string with get

type BaseAccount(currency: Currency, accountFile: FileRepresentation,
                 fromAccountFileToPublicAddress: FileRepresentation -> string) =
    member val AccountFile = accountFile with get

    interface IAccount with
        member val Currency = currency with get
        member val PublicAddress =
            fromAccountFileToPublicAddress accountFile with get

type NormalAccount(currency: Currency, accountFile: FileRepresentation,
                   fromAccountFileToPublicAddress: FileRepresentation -> string) =
    inherit BaseAccount(currency, accountFile, fromAccountFileToPublicAddress)

    member internal self.GetEncryptedPrivateKey() =
        fromAccountFileToPublicAddress accountFile

type ReadOnlyAccount(currency: Currency, accountFile: FileRepresentation,
                     fromAccountFileToPublicAddress: FileRepresentation -> string) =
    inherit BaseAccount(currency, accountFile, fromAccountFileToPublicAddress)

type ArchivedAccount(currency: Currency, accountFile: FileRepresentation,
                     fromAccountFileToPublicAddress: FileRepresentation -> string) =
    inherit BaseAccount(currency, accountFile, fromAccountFileToPublicAddress)

    member internal __.GetUnencryptedPrivateKey() =
        fromAccountFileToPublicAddress accountFile
