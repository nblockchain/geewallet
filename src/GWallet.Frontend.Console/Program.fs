open System

open GWallet.Backend

[<EntryPoint>]
let main argv =
    let ethCurrency = "ETH"
    let account = AccountApi.CreateAccount(ethCurrency)

    Console.WriteLine("** STATUS **")
    let ethStatus = sprintf "%s: Address=[%s]" ethCurrency account.PublicKey
    Console.WriteLine(ethStatus)
    System.Console.ReadLine() |> ignore
    0 // return an integer exit code
