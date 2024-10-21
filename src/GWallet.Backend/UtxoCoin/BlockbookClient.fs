namespace GWallet.Backend

open System.Text.Json

open Fsdk.FSharpUtil

open GWallet.Backend
open GWallet.Backend.UtxoCoin
open GWallet.Backend.FSharpUtil.UwpHacks


/// Client for Blockbook API used by Trezor. See https://github.com/trezor/blockbook/blob/master/docs/api.md
type BlockbookClient(serverAddress: string) =
    inherit RestAPIClient(serverAddress, 1u)

    member self.GetAddressTransactions(address: string): Async<array<BlockchainScriptHashGetHistoryInnerResult>> =
        async {
            let request = SPrintF1 "/api/v2/utxo/%s?confirmed=true" address
            let! response = self.Request request
            let json = JsonDocument.Parse response
            return [| for entry in json.RootElement.EnumerateArray() -> 
                        { TxHash = entry.GetProperty("txid").GetString(); 
                          Height = entry.GetProperty("height").GetUInt64() } |]
        }
