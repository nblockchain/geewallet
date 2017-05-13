open System
open System.Linq
open System.Text.RegularExpressions

open GWallet.Backend

type Options =
    | Exit          = 0
    | Refresh       = 1
    | CreateAccount = 2

let ConvertPascalCaseToSentence(pascalCaseElement: string) =
    Regex.Replace(pascalCaseElement, "[a-z][A-Z]",
                  (fun (m: Match) -> m.Value.[0].ToString() + " " + Char.ToLower(m.Value.[1]).ToString()))

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

// taken from InfraLib
let ConsoleReadPasswordLine() =
    // taken from http://stackoverflow.com/questions/3404421/password-masking-console-application
    let rec ConsoleReadPasswordLineInternal(pwd: string) =
        let key = Console.ReadKey(true)

        if (key.Key = ConsoleKey.Enter) then
            Console.WriteLine()
            pwd
        else

            let newPwd =
                if (key.Key = ConsoleKey.Backspace && pwd.Length > 0) then
                    Console.Write("\b \b")
                    pwd.Substring(0, pwd.Length - 1)
                else
                    Console.Write("*")
                    pwd + key.KeyChar.ToString()
            ConsoleReadPasswordLineInternal(newPwd)

    ConsoleReadPasswordLineInternal(String.Empty)


let rec AskPassword(): string =
    Console.WriteLine()

    Console.Write("Write a password to unlock your account: ")
    let password = ConsoleReadPasswordLine()
    Console.Write("Repeat the password: ")
    let password2 = ConsoleReadPasswordLine()
    if (password <> password2) then
        Console.Error.WriteLine("Passwords are not the same, please try again.")
        AskPassword()
    else
        password

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
                            (account.Currency.ToString())
                            account.PublicAddress
                            (AccountApi.GetBalance(account).ToString())
            Console.WriteLine(status)
    else
        Console.WriteLine("No accounts have been created so far.")

let rec PerformOptions() =
    match AskOption() with
    | Options.Exit -> exit 0
    | Options.CreateAccount ->
        let currency = AskCurrency()
        let password = AskPassword()
        let account = AccountApi.Create currency password
        Console.WriteLine("Account created: " + account.PublicAddress)
    | Options.Refresh -> ()
    | _ -> failwith "Unreachable"

let rec ProgramMainLoop() =
    DisplayStatus()
    PerformOptions()
    ProgramMainLoop()

[<EntryPoint>]
let main argv =
    ProgramMainLoop()

    0 // return an integer exit code
