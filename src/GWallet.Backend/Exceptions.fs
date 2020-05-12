namespace GWallet.Backend

open GWallet.Backend.FSharpUtil

exception InsufficientFunds
exception InsufficientBalanceForFee of Maybe<decimal>

exception InvalidPassword
exception DestinationEqualToOrigin
exception AddressMissingProperPrefix of seq<string>

// with one value means that lenght is mandatory, 2 values means there's a lower limit and upper limit
exception AddressWithInvalidLength of seq<int>

exception AddressWithInvalidChecksum of Maybe<string>
exception AccountAlreadyAdded

exception InvalidDestinationAddress of msg: string

