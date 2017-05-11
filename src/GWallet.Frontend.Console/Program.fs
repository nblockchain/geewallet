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

let rec FindMatchingOption<'T> (optIntroduced, allOptions: ('T*int) list): 'T =
    match Int32.TryParse(optIntroduced) with
    | false, _ -> raise NoOptionFound
    | true, optionParsed ->
        match allOptions with
        | [] -> raise NoOptionFound
        | (head,i)::tail ->
            if (i = optionParsed) then
                head
            else
                FindMatchingOption(optIntroduced, tail)

let rec AskOption(): Options =
    Console.WriteLine()
    Console.WriteLine("Available options:")

    // TODO: move these 2 lines below to FSharpUtil?
    let allOptions = Enum.GetValues(typeof<Options>).Cast<Options>() |> List.ofSeq
    let allOptionsMappedToTheirIntValues = List.map (fun x -> (x, int x)) allOptions

    for option in allOptions do
        Console.WriteLine(sprintf "%d: %s" (int option) (ConvertPascalCaseToSentence (option.ToString())))
    Console.Write("Choose option to perform: ")
    let optIntroduced = System.Console.ReadLine()
    try
        FindMatchingOption(optIntroduced, allOptionsMappedToTheirIntValues)
    with
    | :? NoOptionFound -> AskOption()

let rec AskCurrency(): Currency =
    Console.WriteLine()

    // TODO: move these 2 lines below to FSharpUtil?
    let allCurrencies = Enum.GetValues(typeof<Currency>).Cast<Currency>() |> List.ofSeq
    let allCurrenciesMappedToTheirIntValues = List.map (fun x -> (x, int x)) allCurrencies

    for option in allCurrencies do
        Console.WriteLine(sprintf "%d: %s" (int option) (option.ToString()))
    Console.Write("Select currency: ")
    let optIntroduced = System.Console.ReadLine()
    try
        FindMatchingOption(optIntroduced, allCurrenciesMappedToTheirIntValues)
    with
    | :? NoOptionFound -> AskCurrency()

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
        AccountApi.Create(AskCurrency()) |> ignore
        Console.WriteLine("Account created")
    | _ -> failwith "Unreachable"

let rec ProgramMainLoop() =
    DisplayStatus()
    PerformOptions()
    ProgramMainLoop()

[<EntryPoint>]
let main argv =
    ProgramMainLoop()

    0 // return an integer exit code
