namespace GWallet.Backend

open System

type UnsignedTransactionProposal =
    {
        Currency: Currency;
        OriginAddress: string;
        Amount: decimal;
        DestinationAddress: string;
    }

// NOTE: I wanted to mark this type below `internal`, however that breaks JSON serialization
//       in two possible ways: 1. silently (just returning {}), 2. with an exception
type UnsignedTransaction =
    {
        Proposal: UnsignedTransactionProposal;
        TransactionCount: Int64;
        GasPriceInWei: Int64;
        Cache: CachedNetworkData;
    }