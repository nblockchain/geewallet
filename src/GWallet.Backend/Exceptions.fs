namespace GWallet.Backend

exception InsufficientFunds
exception InsufficientBalanceForFee of Option<decimal>

exception InvalidPassword
exception DestinationEqualToOrigin
exception AddressMissingProperPrefix of seq<string>

type AddressLengthRange =
    {
        Minimum: uint32
        Maximum: uint32
    }
type AddressLength =
    | Fixed of seq<uint32>
    | Variable of AddressLengthRange

exception AddressWithInvalidLength of AddressLength

exception AddressWithInvalidChecksum of Option<string>
exception AccountAlreadyAdded

exception InvalidDestinationAddress of msg: string

exception InvalidJson of content: string

exception TransactionAlreadySigned
exception TransactionNotSignedYet

exception MinerFeeHigherThanOutputs
