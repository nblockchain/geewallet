namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.IO
open System.Security.Cryptography

open Newtonsoft.Json
open NBitcoin
open DotNetLightning.Utils
open DotNetLightning.Payment

open GWallet.Backend
open GWallet.Backend.UtxoCoin
open GWallet.Backend.FSharpUtil


type InvoiceData = {
    PaymentPreimage: PaymentPreimage
    MinFinalCltvExpiry: uint32
    Amount: Money
}

type internal PaymentPreimageConverter() =
    inherit JsonConverter<PaymentPreimage>()

    override __.ReadJson(reader: JsonReader, _: Type, _: PaymentPreimage, _: bool, serializer: JsonSerializer) =
        let serializedPreImage = serializer.Deserialize<string> reader
        serializedPreImage
        |> Convert.FromBase64String
        |> PaymentPreimage.Create

    override __.WriteJson(writer: JsonWriter, state: PaymentPreimage, serializer: JsonSerializer) =
        let serializedPreimage =
            state.ToBytes ()
            |> Seq.toArray
            |> Convert.ToBase64String
        serializer.Serialize(writer, serializedPreimage)


type AccountInvoiceData =
    {
        Invoices: Map<string, InvoiceData>
    }

    static member LightningSerializerSettings =
        let settings = JsonMarshalling.SerializerSettings
        settings.Converters.Add (PaymentPreimageConverter())
        settings

    member internal self.TryGetInvoice (hash: PaymentHash) =
        let hex = DataEncoders.HexEncoder()
        Map.tryFind (hash.ToBytes () |> hex.EncodeData) self.Invoices

    member internal self.AddInvoice (preimage: PaymentPreimage) (invoice: PaymentRequest) =
        let hex = DataEncoders.HexEncoder()
        let paymentHashString =
            preimage.Hash.ToBytes () |> hex.EncodeData

        {
            Invoices =
                self.Invoices
                |> Map.add
                    paymentHashString
                    {
                        PaymentPreimage = preimage
                        MinFinalCltvExpiry = invoice.MinFinalCLTVExpiryDelta.Value
                        Amount = invoice.AmountValue.Value.Satoshi |> Money.Satoshis
                    }
        }

type internal InvoiceDataStore(account: NormalUtxoAccount) =
    static member InvoiceDataFileName = "invoices.json"

    member val Account = account
    member val Currency = (account :> IAccount).Currency

    member self.AccountDir: DirectoryInfo =
        Config.GetConfigDir self.Currency AccountKind.Normal

    member self.ChannelDir: DirectoryInfo =
        Path.Combine (self.AccountDir.FullName, Settings.ConfigDirName)
        |> DirectoryInfo

    member self.InvoiceDataFilePath: string =
        Path.Combine(
            self.ChannelDir.FullName,
            InvoiceDataStore.InvoiceDataFileName
        )

    member internal self.LoadInvoiceData(): AccountInvoiceData =
        try
            let fileName = self.InvoiceDataFilePath
            let json = File.ReadAllText fileName
            Marshalling.DeserializeCustom<AccountInvoiceData> (
                json,
                AccountInvoiceData.LightningSerializerSettings
            )
        with
        | :? FileNotFoundException | :? DirectoryNotFoundException ->
            {
                AccountInvoiceData.Invoices = Map.empty
            }

    // For now all lightning incoming messages are handled within a single thread, we don't need a lock here.
    member internal self.SaveInvoiceData (invoiceDataToSave: AccountInvoiceData) =
        let fileName = self.InvoiceDataFilePath
        let json =
            Marshalling.SerializeCustom(
                invoiceDataToSave,
                AccountInvoiceData.LightningSerializerSettings,
                Marshalling.DefaultFormatting
            )
        if not self.ChannelDir.Exists then
            self.ChannelDir.Create()
        File.WriteAllText(fileName, json)

type InvoiceManagement (account: NormalUtxoAccount, password: string) =

    member self.CreateInvoice (amountInSatoshis: uint64) (description: string) =

        let rngEngine = RandomNumberGenerator.Create()
        let preImage =
            let preImageBytes = Array.zeroCreate PaymentPreimage.LENGTH
            rngEngine.GetNonZeroBytes preImageBytes
            preImageBytes |> PaymentPreimage.Create

        let currency = (account :> IAccount).Currency
        let network = UtxoCoin.Account.GetNetwork currency

        let nodeMasterPrivKey =
            (Account.GetPrivateKey account password).ToBytes()
            |> NBitcoin.ExtKey.CreateFromSeed
            |> NodeMasterPrivKey

        let invoiceFields =
            {
                TaggedFields.Fields =
                    [
                        TaggedField.PaymentHashTaggedField preImage.Hash
                        TaggedField.MinFinalCltvExpiryTaggedField (BlockHeightOffset32 40u)
                        TaggedField.ExpiryTaggedField (DateTimeOffset.UtcNow.AddYears 1)
                        TaggedField.FeaturesTaggedField (Settings.SupportedFeatures currency None)
                        TaggedField.DescriptionTaggedField description
                    ]
            }

        let paymentRequest = 
            UnwrapResult (PaymentRequest.TryCreate (network, LNMoney.Satoshis amountInSatoshis |> Some, DateTimeOffset.UtcNow, invoiceFields, nodeMasterPrivKey.RawExtKey().PrivateKey)) "failed to create the invoice"

        let invoiceStore = InvoiceDataStore(account)
        let invoiceData =
            invoiceStore
                .LoadInvoiceData()
                .AddInvoice preImage paymentRequest
        invoiceStore.SaveInvoiceData invoiceData

        paymentRequest.ToString()
