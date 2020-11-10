namespace GWallet.Backend.Tests.EndToEnd

open System.Net // For IPAddress and IPEndPoint

open Newtonsoft.Json // For JsonConvert
open NUnit.Framework
open BTCPayServer.Lightning
open BTCPayServer.Lightning.LND
open NBitcoin // For ExtKey
open DotNetLightning.Utils

open GWallet.Backend
open GWallet.Backend.UtxoCoin.Lightning
open GWallet.Backend.FSharpUtil.UwpHacks

module Config =
    let FundeeAccountsPrivateKey =
        // Note: The key needs to be hard-coded, as opposed to randomly
        // generated, since it is used in two separate processes and must be
        // the same in each process.
        new Key(uint256.Parse("9d1ee30acb68716ed5f4e25b3c052c6078f1813f45d33a47e46615bfd05fa6fe").ToBytes())
    let FundeeNodePubKey =
        let extKey = Node.AccountPrivateKeyToNodeSecret FundeeAccountsPrivateKey
        extKey.PrivateKey.PubKey
    let FundeeLightningIPEndpoint = IPEndPoint (IPAddress.Parse "127.0.0.1", 9735)


    let WalletToWalletTestPayment0Amount = Money(0.01m, MoneyUnit.BTC)
    let WalletToWalletTestPayment1Amount = Money(0.015m, MoneyUnit.BTC)

    let MinimumDepth = (BlockHeightOffset32 (uint32 7))

    let FundeeNodeEndpoint =
        NodeEndPoint.Parse
            Currency.BTC
            (SPrintF3
                "%s@%s:%d"
                (FundeeNodePubKey.ToHex())
                (FundeeLightningIPEndpoint.Address.ToString())
                FundeeLightningIPEndpoint.Port
            )
