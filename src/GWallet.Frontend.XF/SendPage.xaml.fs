namespace GWallet.Frontend.XF

open System
open System.Linq
open System.Threading
open System.Threading.Tasks

open Xamarin.Forms
open Xamarin.Forms.Xaml
open Xamarin.Essentials
open ZXing.Net.Mobile.Forms

open GWallet.Backend
open GWallet.Backend.FSharpUtil.UwpHacks

type TransactionInfo =
    { Metadata: IBlockchainFeeInfo;
      Destination: string; 
      Amount: TransferAmount;
    }

type TransactionProposal<'T when 'T :> IBlockchainFeeInfo> =
    // hot wallet dealing with normal or readonly account:
    | NotAvailableBecauseOfHotMode
    // cold wallet about to scan proposal from hot wallet:
    | ColdStorageMode of Option<UnsignedTransaction<'T>>
    // hot wallet about to broadcast transaction of ReadOnly account:
    | ColdStorageRemoteControl of Option<SignedTransaction<'T>>

type SendPage(account: IAccount, receivePage: Page, newReceivePageFunc: unit->Page) =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<SendPage>)

    do
        ()