namespace GWallet.Backend.Tests.End2End

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Security

open NBitcoin

open GWallet.Backend
open GWallet.Backend.UtxoCoin.Lightning
open GWallet.Backend.FSharpUtil.UwpHacks

open FSharpUtil

[<RequireQualifiedAccess>]
module Config =

    let CredentialsFilePath = "../../../WslCredentials.dat"

    let CredentialsUnsecuredOpt =
        if Environment.OSVersion.Platform <> PlatformID.Unix then
            if File.Exists CredentialsFilePath then
                let credentialsText = File.ReadAllText CredentialsFilePath
                match credentialsText.Split ([|Environment.NewLine|], StringSplitOptions.None) with
                | [|userName; password|] -> Some (userName, password)
                | _ -> None
            else None
        else None

    let CredentialsSecuredOpt =
        match CredentialsUnsecuredOpt with
        | Some (userName, password) ->
            let passwordSecure = new SecureString ()
            for c in password do passwordSecure.AppendChar c
            Some (userName, passwordSecure)
        | None -> None

    let WebPort = "80"

    let HttpPort = "8080"

    let GlobalIP = "0.0.0.0"

    let LocalHostIP = "127.0.0.1"

    let WindowsHostIP =
        if Environment.OSVersion.Platform <> PlatformID.Unix then
            let (userName, password) = UnwrapOption CredentialsSecuredOpt "Missing WSL credentials."
            let startInfo = 
                ProcessStartInfo (
                    UseShellExecute = false,
                    FileName = "wsl.exe",
                    Arguments = "cat /etc/resolv.conf",
                    RedirectStandardOutput = true,
                    UserName = userName,
                    Password = password)
            use proc = new Process (StartInfo = startInfo)
            if proc.Start () then
                let mutable nameServerLineOpt = None
                while not proc.StandardOutput.EndOfStream do
                    let line = proc.StandardOutput.ReadLine().Trim()
                    if line.Length <> 0 && line.[0] <> '#' && (line.Split ' ').[0] = "nameserver" then
                        nameServerLineOpt <- Some line
                match nameServerLineOpt with
                | Some nameServerLine -> (nameServerLine.Split ' ').[1]
                | None -> LocalHostIP
            else LocalHostIP
        else LocalHostIP

    let WslHostIP =
        if Environment.OSVersion.Platform <> PlatformID.Unix then
            let (userName, password) = UnwrapOption CredentialsSecuredOpt "Missing WSL credentials."
            let procStart =
                ProcessStartInfo (
                    UseShellExecute = false,
                    FileName = "wsl.exe",
                    Arguments = "hostname -I",
                    RedirectStandardOutput = true,
                    UserName = userName,
                    Password = password)
            use proc = new Process (StartInfo = procStart)
            if proc.Start () then
                let mutable ipOpt = None
                while not proc.StandardOutput.EndOfStream do
                    let line = proc.StandardOutput.ReadLine().Trim()
                    ipOpt <- Some line
                Option.defaultValue LocalHostIP ipOpt
            else LocalHostIP
        else LocalHostIP

    let FundeeAccountsPrivateKey =
        // Note: The key needs to be hard-coded, as opposed to randomly
        // generated, since it is used in two separate processes and must be
        // the same in each process.
        new Key (uint256.Parse("9d1ee30acb68716ed5f4e25b3c052c6078f1813f45d33a47e46615bfd05fa6fe").ToBytes())

    let private fundeeNodePubKey =
        let extKey = FundeeAccountsPrivateKey.ToBytes() |> ExtKey
        extKey.PrivateKey.PubKey

    let FundeeLightningIPEndpoint = IPEndPoint (IPAddress.Parse "127.0.0.1", 9735)

    let FundeeNodeEndpoint =
        NodeEndPoint.Parse
            Currency.BTC
            (SPrintF3
                "%s@%s:%d"
                (fundeeNodePubKey.ToHex())
                (FundeeLightningIPEndpoint.Address.ToString())
                FundeeLightningIPEndpoint.Port
            )
