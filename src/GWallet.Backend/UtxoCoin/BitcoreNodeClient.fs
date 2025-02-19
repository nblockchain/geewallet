namespace GWallet.Backend

open System.Text.Json

open Fsdk.FSharpUtil

open GWallet.Backend
open GWallet.Backend.UtxoCoin
open GWallet.Backend.FSharpUtil.UwpHacks


/// see https://github.com/bitpay/bitcore/blob/master/packages/bitcore-node/docs/api-documentation.md
type BitcoreNodeClient(serverAddress: string) =
    inherit RestAPIClient(serverAddress, 1u)

    member self.GetAddressTransactions(address: string): Async<array<BlockchainScriptHashGetHistoryInnerResult>> =
        async {
            let request = SPrintF1 "/api/BTC/mainnet/address/%s/txs" address
            let! response = self.Request request
            let json = JsonDocument.Parse response
            return [| for entry in json.RootElement.EnumerateArray() -> 
                        { TxHash = entry.GetProperty("mintTxid").GetString(); 
                          Height = entry.GetProperty("mintHeight").GetUInt64() } |]
        }
