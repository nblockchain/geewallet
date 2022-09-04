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
        Url: string
    }

    static member private GetUri (url: string) =
        UriBuilder(url).Uri

    static member IsOnionConnection(text: string): bool =
        let uri = NOnionEndPoint.GetUri text
        uri.Host.EndsWith ".onion"

    static member Parse (currency: Currency) (text: string) : NOnionEndPoint =
        if not (NOnionEndPoint.IsOnionConnection text) then
            raise <| FormatException "Not an onion address"
        let uri = NOnionEndPoint.GetUri text

        {
            NodeId = PublicKey.Parse currency uri.UserInfo
            Url = uri.Authority
        }

    override self.ToString() =
        SPrintF2 "%s@%s"
            (self.NodeId.ToString())
            self.Url

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
    | Tor of hostUrl: string

type NodeTransportType =
    | Server of NodeServerType
    | Client of NodeClientType
