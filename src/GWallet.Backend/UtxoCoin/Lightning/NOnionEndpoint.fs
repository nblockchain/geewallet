namespace GWallet.Backend.UtxoCoin.Lightning

open System
open System.Web
open System.Net

open NOnion.Network
open NOnion.Services

open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks

type NOnionEndpoint =
    {
        NodeId: PublicKey
        IntroductionPoint: IntroductionPointPublicInfo
    }

    static member IsNOnionConnection(text: string): bool =
        text.StartsWith "geewallet+nonion://"

    static member Parse (currency: Currency) (text: string) : NOnionEndpoint =
        if not (NOnionEndpoint.IsNOnionConnection text) then
            raise <| FormatException "Not an onion address"

        let uri = System.Uri text
        let queryStringSegments = HttpUtility.ParseQueryString uri.Query

        let encryptionKey = queryStringSegments.["EncryptionKey"]
        let authKey = queryStringSegments.["AuthKey"]
        let onionKey = queryStringSegments.["OnionKey"]
        let fingerPrint = queryStringSegments.["Fingerprint"]
        let masterPublicKey = queryStringSegments.["MasterPublicKey"]

        // TODO: ask Afshin for the stackoverflow link
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
    | EndPoint of NodeEndPoint
    | NEndpoint of NOnionEndpoint

    override self.ToString() =
        match self with
        | NodeIdentifier.EndPoint endpoint ->
            endpoint.ToString()
        | NodeIdentifier.NEndpoint nendpoint ->
            nendpoint.ToString()

type NodeServerType =
    | Tcp of Option<IPEndPoint>
    | Tor
