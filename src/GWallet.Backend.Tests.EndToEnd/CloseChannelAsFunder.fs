namespace GWallet.Backend.Tests.EndToEnd

open NUnit.Framework
open DotNetLightning.Utils
open ResultUtils.Portability

open GWallet.Backend
open GWallet.Backend.UtxoCoin // For NormalUtxoAccount
open GWallet.Backend.UtxoCoin.Lightning
open GWallet.Backend.FSharpUtil.UwpHacks


[<TestFixture>]
type CloseChannelAsFunder() =
    
    [<SetUp>]
    member __.SetUp () =
        do Config.SetRunModeTesting()

    [<Test>]
    member __.``can close channel from LND (as funder)``() = Async.RunSynchronously <| async {
        use! walletInstance = WalletInstance.New None None
        use bitcoind = Bitcoind.Start()
        use _electrumServer = ElectrumServer.Start bitcoind
        use! lnd = Lnd.Start bitcoind

        let! maybeChannelId  =
            try 
                ChannelManagement.OpenChannel walletInstance bitcoind lnd
            with
            | ex ->
                async {
                    let res: Option<ChannelIdentifier> = None
                    return res
                }

        match maybeChannelId with 
        | None -> Assert.Inconclusive "test cannot be run because channel opening failed"
        | Some channelId ->
            let! closeChannelRes = Lightning.Network.CloseChannel walletInstance.Node channelId
            match closeChannelRes with
            | Ok _ -> ()
            | Error err -> failwith (SPrintF1 "error when closing channel: %s" err.Message)

            match (walletInstance.ChannelStore.ChannelInfo channelId).Status with
            | ChannelStatus.Closing -> ()
            | status -> failwith (SPrintF1 "unexpected channel status. Expected Closing, got %A" status)

            // Mine 10 blocks to make sure closing tx is confirmed
            bitcoind.GenerateBlocks (BlockHeightOffset32 (uint32 10)) walletInstance.Address
        
            let rec waitForClosingTxConfirmed attempt = async {
                Infrastructure.LogDebug (SPrintF1 "Checking if closing tx is finished, attempt #%d" attempt)
                if attempt = 10 then
                    return Error "Closing tx not confirmed after maximum attempts"
                else
                    let! txIsConfirmed = Lightning.Network.CheckClosingFinished (walletInstance.ChannelStore.ChannelInfo channelId)
                    if txIsConfirmed then
                        return Ok ()
                    else
                        do! Async.Sleep 1000
                        return! waitForClosingTxConfirmed (attempt + 1)
                    
            }

            let! closingTxConfirmedRes = waitForClosingTxConfirmed 0
            match closingTxConfirmedRes with
            | Ok _ -> ()
            | Error err -> failwith (SPrintF1 "error when waiting for closing tx to confirm: %s" err)
        }
