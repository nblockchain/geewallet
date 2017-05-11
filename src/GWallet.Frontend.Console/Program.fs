open System

open GWallet.Backend

[<EntryPoint>]
let main argv =
    let ethCurrency = Currency.ETH
    let account = AccountApi.CreateOrGetMainAccount(ethCurrency)

    Console.WriteLine("** STATUS **")
    let ethStatus = sprintf "%s: Address=[%s] Balance=[%s]"
                        (ethCurrency.ToString()) account.PublicAddress (AccountApi.GetBalance(account).ToString())
    Console.WriteLine(ethStatus)
    System.Console.ReadLine() |> ignore
    0 // return an integer exit code
