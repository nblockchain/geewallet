namespace GWallet.Backend

open System.IO

type WatchWalletInfo =
    {
        UtxoCoinPublicKey: string
        EtherPublicAddress: string
        LightningNodeMasterPrivKey: string
    }

type FileRepresentation =
    {
        Name: string;
        Content: unit->string;
    }
    static member FromFile (file: FileInfo) =
        {
            Name = Path.GetFileName file.FullName
            Content = (fun _ -> File.ReadAllText file.FullName)
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
    static member All() =
        seq {
            yield Normal
            yield ReadOnly
            yield Archived
        }

type IAccount =
    abstract member Currency: Currency with get
    abstract member PublicAddress: string with get
    abstract member Kind: AccountKind with get
    abstract member AccountFile: FileRepresentation with get

type NormalAccount(currency: Currency, accountFile: FileRepresentation,
                   fromAccountFileToPublicAddress: FileRepresentation -> string) =
    member internal __.GetEncryptedPrivateKey() =
        accountFile.Content()

    interface IAccount with
        member val Kind = AccountKind.Normal
        member val AccountFile = accountFile
        member val Currency = currency
        member val PublicAddress = fromAccountFileToPublicAddress accountFile

type ReadOnlyAccount(currency: Currency, accountFile: FileRepresentation,
                     fromAccountFileToPublicAddress: FileRepresentation -> string) =
    interface IAccount with
        member val Kind = AccountKind.ReadOnly
        member val AccountFile = accountFile
        member val Currency = currency
        member val PublicAddress = fromAccountFileToPublicAddress accountFile


type ArchivedAccount(currency: Currency, accountFile: FileRepresentation,
                     fromAccountFileToPublicAddress: FileRepresentation -> string) =
    member internal __.GetUnencryptedPrivateKey() =
        accountFile.Content()

    interface IAccount with
        member val Kind = AccountKind.Archived
        member val AccountFile = accountFile
        member val Currency = currency
        member val PublicAddress = fromAccountFileToPublicAddress accountFile
