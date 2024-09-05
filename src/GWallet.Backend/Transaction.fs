namespace GWallet.Backend

open Newtonsoft.Json

type ITransactionDetails =
    abstract member OriginMainAddress: string
    abstract member Amount: decimal
    abstract member Currency: Currency
    abstract member DestinationAddress: string

type internal SignedTransactionDetails =
    {
        OriginMainAddress: string

        Amount: decimal
        Currency: Currency
        DestinationAddress: string
    }
    interface ITransactionDetails with
        member self.OriginMainAddress = self.OriginMainAddress
        member self.Amount = self.Amount
        member self.Currency = self.Currency
        member self.DestinationAddress = self.DestinationAddress

type UnsignedTransactionProposal =
    {
        OriginMainAddress: string

        Amount: TransferAmount;
        DestinationAddress: string;
    }
    interface ITransactionDetails with
        member self.OriginMainAddress = self.OriginMainAddress
        member self.Amount = self.Amount.ValueToSend
        member self.Currency = self.Amount.Currency
        member self.DestinationAddress = self.DestinationAddress

// NOTE: I wanted to mark this type below `internal`, however that breaks JSON serialization
//       in two possible ways: 1. silently (just returning {}), 2. with an exception
type UnsignedTransaction<'T when 'T:> IBlockchainFeeInfo> =
    {
        Proposal: UnsignedTransactionProposal;
        Metadata: 'T;
        Cache: DietCache;
    }
    member self.ToAbstract(): UnsignedTransaction<IBlockchainFeeInfo> =
        {
            Metadata = self.Metadata :> IBlockchainFeeInfo;
            Cache = self.Cache;
            Proposal = self.Proposal;
        }

type SignedTransaction<'T when 'T:> IBlockchainFeeInfo> =
    {
        TransactionInfo: UnsignedTransaction<'T>;
        RawTransaction: string;
    }
    member self.ToAbstract(): SignedTransaction<IBlockchainFeeInfo> =
        {
            TransactionInfo = self.TransactionInfo.ToAbstract();
            RawTransaction = self.RawTransaction;
        }

type ImportedTransaction<'T when 'T:> IBlockchainFeeInfo> =
| Unsigned of UnsignedTransaction<'T>
| Signed of SignedTransaction<'T>
