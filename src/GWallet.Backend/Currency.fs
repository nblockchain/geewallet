namespace GWallet.Backend

type Currency =
    | ETH
    | ETC
    static member ToStrings() =
        Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(typeof<Currency>)
            |> Array.map (fun info -> info.Name)
    static member GetAll() =
        FSharpUtil.GetAllElementsFromDiscriminatedUnion<Currency>()
    override self.ToString() =
        sprintf "%A" self


