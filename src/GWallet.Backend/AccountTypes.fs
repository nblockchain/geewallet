namespace GWallet.Backend

type WatchWalletInfo =
    {
        UtxoCoinPublicKey: string
        EtherPublicAddress: string
    }

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

    member internal self.GetEncryptedPrivateKey() =
        accountFile.Content()

    override this.Kind = AccountKind.Normal

type ReadOnlyAccount(currency: Currency, accountFile: FileRepresentation,
                     fromAccountFileToPublicAddress: FileRepresentation -> string) =
    inherit BaseAccount(currency, accountFile, fromAccountFileToPublicAddress)

    override this.Kind = AccountKind.ReadOnly

type ArchivedAccount(currency: Currency, accountFile: FileRepresentation,
                     fromAccountFileToPublicAddress: FileRepresentation -> string) =
    inherit BaseAccount(currency, accountFile, fromAccountFileToPublicAddress)

    member internal __.GetUnencryptedPrivateKey() =
        accountFile.Content()

    override this.Kind = AccountKind.Archived
