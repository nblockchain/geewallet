
namespace GWallet.Backend

type PublicKey(pubKey: string, currency: Currency) =
    do
        if currency.IsUtxo() then
            NBitcoin.PubKey pubKey |> ignore

    override __.ToString() =
        pubKey

