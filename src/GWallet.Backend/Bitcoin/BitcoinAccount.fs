namespace GWallet.Backend.Bitcoin

// NOTE: we can rename this file to less redundant "Account.fs" when this F# compiler bug is fixed:
// https://github.com/Microsoft/visualfsharp/issues/3231

open System
open System.IO

open NBitcoin

open GWallet.Backend

module internal Account =

    let GetPublicAddressFromAccountFile (accountFile: FileInfo) =
        let pubKey = new PubKey(accountFile.Name)
        pubKey.GetAddress(Network.Main).ToString()

    // TODO: return MaybeCached<decimal>
    let GetBalance(account: IAccount): decimal =
        let electrumServer = ElectrumServer.PickRandom()
        use electrumClient = new ElectrumClient(electrumServer)
        electrumClient.GetBalance account.PublicAddress |> UnitConversion.FromSatoshiToBTC

    let Create password =
        let privkey = Key()
        let secret = privkey.GetBitcoinSecret(Network.Main)
        let encryptedSecret = secret.PrivateKey.GetEncryptedBitcoinSecret(password, Network.Main)
        let encryptedPrivateKey = encryptedSecret.ToWif()
        let publicKey = secret.PubKey.ToString()
        publicKey,encryptedPrivateKey

    let ValidateAddress (address: string) =
        let BITCOIN_ADDRESSES_LENGTH = 34
        let BITCOIN_ADDRESS_PREFIX = "1"

        if not (address.StartsWith(BITCOIN_ADDRESS_PREFIX)) then
            raise (AddressMissingProperPrefix(BITCOIN_ADDRESS_PREFIX))

        if (address.Length <> BITCOIN_ADDRESSES_LENGTH) then
            raise (AddressWithInvalidLength(BITCOIN_ADDRESSES_LENGTH))

        // FIXME: add bitcoin checksum algorithm?
