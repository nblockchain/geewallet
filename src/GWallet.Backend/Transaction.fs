namespace GWallet.Backend

open System

type UnsignedTransactionProposal =
    {
        Currency: Currency;
        OriginAddress: string;
        Amount: TransferAmount;
        DestinationAddress: string;
    }

// NOTE: I wanted to mark this type below `internal`, however that breaks JSON serialization
//       in two possible ways: 1. silently (just returning {}), 2. with an exception
type UnsignedTransaction<'T when 'T :> IBlockchainFee> =
    {
        Proposal: UnsignedTransactionProposal;
        TransactionCount: Int64;
        Fee: 'T;
        Cache: CachedNetworkData;
    }

type SignedTransaction<'T when 'T :> IBlockchainFee> =
    {
        TransactionInfo: UnsignedTransaction<'T>;
        RawTransaction: string;
    }