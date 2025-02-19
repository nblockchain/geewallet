namespace GWallet.Backend

open System.IO

type UtxoPublicKey = string

type WatchWalletInfo =
    {
        UtxoCoinPublicKey: UtxoPublicKey
        EtherPublicAddress: string
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
    | Ephemeral
    static member All() =
        seq {
            yield Normal
            yield ReadOnly
            yield Archived
        }

type IAccount =
    abstract member Currency: Currency with get
    abstract member PublicAddress: string with get

[<AbstractClass>]
type BaseAccount(currency: Currency, accountFile: FileRepresentation,
                 fromAccountFileToPublicAddress: FileRepresentation -> string) =
    member val AccountFile = accountFile with get

    abstract member Kind: AccountKind

    interface IAccount with
        member val Currency = currency with get
        member val PublicAddress =
            fromAccountFileToPublicAddress accountFile with get


type NormalAccount(currency: Currency, accountFile: FileRepresentation,
                   fromAccountFileToPublicAddress: FileRepresentation -> string) =
    inherit BaseAccount(currency, accountFile, fromAccountFileToPublicAddress)

    member internal __.GetEncryptedPrivateKey() =
        accountFile.Content()

    override __.Kind = AccountKind.Normal

type ReadOnlyAccount(currency: Currency, accountFile: FileRepresentation,
                     fromAccountFileToPublicAddress: FileRepresentation -> string) =
    inherit BaseAccount(currency, accountFile, fromAccountFileToPublicAddress)

    override __.Kind = AccountKind.ReadOnly

type ArchivedAccount(currency: Currency, accountFile: FileRepresentation,
                     fromAccountFileToPublicAddress: FileRepresentation -> string) =
    inherit BaseAccount(currency, accountFile, fromAccountFileToPublicAddress)

    member internal __.GetUnencryptedPrivateKey() =
        accountFile.Content()

    override __.Kind = AccountKind.Archived
