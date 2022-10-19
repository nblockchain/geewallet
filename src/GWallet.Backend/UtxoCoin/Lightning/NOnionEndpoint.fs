namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Web
open System.Net

open DotNetLightning.Utils
open NOnion.Network
open NOnion.Services

open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks

type NOnionEndPoint =
    {
        NodeId: PublicKey
        IntroductionPoint: IntroductionPointPublicInfo
    }

    static member IsNOnionConnection(text: string): bool =
        text.StartsWith "geewallet+nonion://"

    static member Parse (currency: Currency) (text: string) : NOnionEndPoint =
        if not (NOnionEndPoint.IsNOnionConnection text) then
            raise <| FormatException "Not an onion address"

        let uri = System.Uri text
        let queryStringSegments = HttpUtility.ParseQueryString uri.Query

        let encryptionKey = queryStringSegments.["EncryptionKey"]
        let authKey = queryStringSegments.["AuthKey"]
        let onionKey = queryStringSegments.["OnionKey"]
        let fingerPrint = queryStringSegments.["Fingerprint"]
        let masterPublicKey = queryStringSegments.["MasterPublicKey"]

        // Passing base64 encoded strings in URL: https://stackoverflow.com/a/5835352/4824925
        let replaceForDecode (text: string) =
            text.Replace('.', '+').Replace('_', '/').Replace('-', '=')

        {
            NodeId = PublicKey.Parse currency uri.UserInfo
            IntroductionPoint =
                {
                    Address = uri.Host
                    Port = uri.Port
                    EncryptionKey = replaceForDecode encryptionKey
                    AuthKey = replaceForDecode authKey
                    OnionKey = replaceForDecode onionKey
                    Fingerprint = replaceForDecode fingerPrint
                    MasterPublicKey = replaceForDecode masterPublicKey
                }
        }

    override self.ToString() =
        // Passing base64 encoded strings in URL: https://stackoverflow.com/a/5835352/4824925
        let replaceForEncode (text: string) =
            text.Replace('+', '.').Replace('/', '_').Replace('=', '-')

        let introPoint = self.IntroductionPoint
        SPrintF8 "geewallet+nonion://%s@%s:%d?EncryptionKey=%s&AuthKey=%s&OnionKey=%s&Fingerprint=%s&MasterPublicKey=%s"
            (self.NodeId.ToString())
            introPoint.Address
            introPoint.Port
            (replaceForEncode introPoint.EncryptionKey)
            (replaceForEncode introPoint.AuthKey)
            (replaceForEncode self.IntroductionPoint.OnionKey)
            (replaceForEncode introPoint.Fingerprint)
            (replaceForEncode introPoint.MasterPublicKey)

type NodeIdentifier =
    | TcpEndPoint of NodeEndPoint
    | TorEndPoint of NOnionEndPoint

    member internal self.NodeId =
        match self with
        | NodeIdentifier.TcpEndPoint nodeEndPoint ->
            nodeEndPoint.NodeId.ToString()
        | NodeIdentifier.TorEndPoint nonionEndPoint ->
            nonionEndPoint.NodeId.ToString()
        |> NBitcoin.PubKey
        |> NodeId


    override self.ToString() =
        match self with
        | NodeIdentifier.TcpEndPoint endPoint ->
            endPoint.ToString()
        | NodeIdentifier.TorEndPoint torEndPoint ->
            torEndPoint.ToString()

type NodeServerType =
    | Tcp of bindAddress: IPEndPoint
    | Tor

type NodeClientType =
    | Tcp of counterPartyIP: IPEndPoint
    | Tor

type NodeTransportType =
    | Server of NodeServerType
    | Client of NodeClientType
