namespace GWallet.Backend

type UnsignedDecimal(value: decimal) =
    do
        if value <= 0m then
            invalidArg "value" "Amount has to be above zero"

    member this.Value
        with get() = value

    static member (+) (d1: UnsignedDecimal, d2: UnsignedDecimal) =
        UnsignedDecimal(d1.Value + d2.Value)

    static member (-) (d1: UnsignedDecimal, d2: UnsignedDecimal) =
        UnsignedDecimal(d1.Value - d2.Value)

    static member Zero =
        UnsignedDecimal 0m
