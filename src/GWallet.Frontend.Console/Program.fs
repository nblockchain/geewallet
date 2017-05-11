open System

open GWallet.Backend

[<EntryPoint>]
let main argv =
    let ethCurrency = Currency.ETH
    let account = AccountApi.CreateOrGetMainAccount(ethCurrency)

    Console.WriteLine("** STATUS **")
    let ethStatus = sprintf "%s: Address=[%s]" (ethCurrency.ToString()) account.PublicAddress
    Console.WriteLine(ethStatus)
    System.Console.ReadLine() |> ignore
    0 // return an integer exit code
