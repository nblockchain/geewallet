namespace GWallet.Backend

exception InsufficientFunds
exception InsufficientFee of msg: string // <- right now only used in the case of out of gas
exception InsufficientBalanceForFee of decimal

exception InvalidPassword
exception DestinationEqualToOrigin
exception AddressMissingProperPrefix of seq<string>

// with one value means that lenght is mandatory, 2 values means there's a lower limit and upper limit
exception AddressWithInvalidLength of seq<int>

exception AddressWithInvalidChecksum of Option<string>
exception AccountAlreadyAdded
