open System
open System.Linq
open System.Text.RegularExpressions

open GWallet.Backend

type Options =
    | Exit          = 0
    | CreateAccount = 1

let ConvertPascalCaseToSentence(pascalCaseElement: string) =
    Regex.Replace(pascalCaseElement, "[a-z][A-Z]", (fun (m: Match) -> m.Value.[0].ToString() + " " + Char.ToLower(m.Value.[1]).ToString()))

exception NoOptionFound

let rec AskOption(): Options =
    let rec findMatchingOption (optIntroduced, allOptions) =
        match Int32.TryParse(optIntroduced) with
        | false, _ -> raise NoOptionFound
        | true, optionParsed ->
            match allOptions with
            | [] -> raise NoOptionFound
            | head::tail ->
                if (int head = optionParsed) then
                    head
                else
                    findMatchingOption(optIntroduced, tail)

    Console.WriteLine()
    Console.WriteLine("Available options:")
    let allOptions = Enum.GetValues(typeof<Options>).Cast<Options>() |> List.ofSeq
    for option in allOptions do
        Console.WriteLine(sprintf "%d: %s" (int option) (ConvertPascalCaseToSentence (option.ToString())))
    Console.Write("Choose option to perform: ")
    let optIntroduced = System.Console.ReadLine()
    try
        findMatchingOption(optIntroduced, allOptions)
    with
    | :? NoOptionFound -> AskOption()

let DisplayStatus() =
    Console.WriteLine("** STATUS **")
    let accounts = AccountApi.GetAllAccounts()
    if (accounts.Any()) then
        for account in accounts do
            let status = sprintf "%s: Address=[%s] Balance=[%s]"
                            (account.Currency.ToString()) account.PublicAddress (AccountApi.GetBalance(account).ToString())
            Console.WriteLine(status)
    else
        Console.WriteLine("No accounts have been created so far.")

let rec PerformOptions() =
    match AskOption() with
    | Options.Exit -> exit 0
    | Options.CreateAccount ->
        AccountApi.Create(Currency.ETH) |> ignore
        Console.WriteLine("Account created")

let rec ProgramMainLoop() =
    DisplayStatus()
    PerformOptions()
    ProgramMainLoop()

[<EntryPoint>]
let main argv =
    ProgramMainLoop()

    0 // return an integer exit code
