namespace GWallet.Backend.Bitcoin

open System

type OutputInfo =
    {
        ValueInSatoshis: int64;
        DestinationAddress: string;
    }

type RawTransactionOutpoint =
    {
        RawTransaction: string;
        OutputIndex: int;
    }

type TransactionDraft =
    {
        Inputs: list<RawTransactionOutpoint>;
        Outputs: list<OutputInfo>;
    }
