open System

open GWallet.Backend

[<EntryPoint>]
let main argv =
    let account = Account.CreateAccount("ETH")
    Console.WriteLine(snd account)
    System.Console.ReadLine() |> ignore
    0 // return an integer exit code
