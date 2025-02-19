namespace GWallet.Backend.UtxoCoin

open System
open System.Linq

open NBitcoin
open Org.BouncyCastle.Crypto
open Org.BouncyCastle.Math

open GWallet.Backend.FSharpUtil.UwpHacks


type private SilentPaymentAddressEncoder(hrp: string) =
    inherit DataEncoders.Bech32Encoder(DataEncoders.ASCIIEncoder().DecodeData hrp, StrictLength = false)

    member self.DecodeData(encoded: string): array<byte> * byte =
        let rawData, _ = self.DecodeDataRaw encoded
        let bitsInByte = 8
        let bitsPerCharacter = 5
        let decoded = self.ConvertBits(rawData.Skip 1, bitsPerCharacter, bitsInByte, false);
        decoded, rawData.[0]

type SilentPaymentAddress = 
    {
        ScanPublicKey: PubKey
        SpendPublicKey: PubKey
    }

    static member MainNetPrefix = "sp"
    static member TestNetPrefix = "tsp"

    // https://github.com/bitcoin/bips/blob/master/bip-0352.mediawiki#address-encoding
    static member MinimumEncodedLength = 116u
    static member MaximumEncodedLength = 1023u

    static member private GetEncoder(chainName: ChainName) =
        let hrp =
            if chainName = ChainName.Mainnet then
                SilentPaymentAddress.MainNetPrefix
            elif chainName = ChainName.Testnet then
                SilentPaymentAddress.TestNetPrefix
            else
                failwith "Only Mainnet and Testnet are supported for SilentPayment address"
        SilentPaymentAddressEncoder hrp

    static member IsSilentPaymentAddress (address: string) =
        address.StartsWith SilentPaymentAddress.MainNetPrefix 
        || address.StartsWith SilentPaymentAddress.MainNetPrefix

    member self.Encode(network: Network) : string =
        let encoder = SilentPaymentAddress.GetEncoder network.ChainName
        let data = 
            let versionByte = 0uy // version 0
            Array.append
                [| versionByte |]
                (Array.append (self.ScanPublicKey.ToBytes()) (self.SpendPublicKey.ToBytes()))
        encoder.EncodeData(data, 0, data.Length, DataEncoders.Bech32EncodingType.BECH32M)

    static member Decode(encodedAddress: string) : SilentPaymentAddress =
        let chain =
            if encodedAddress.StartsWith SilentPaymentAddress.TestNetPrefix then
                ChainName.Testnet
            elif encodedAddress.StartsWith SilentPaymentAddress.MainNetPrefix then
                ChainName.Mainnet
            else
                failwith "Encoded SilentPayment address should start with tsp or sp"
        let encoder = SilentPaymentAddress.GetEncoder chain
        let data, versionByte = encoder.DecodeData encodedAddress
        if versionByte = 31uy then
            raise <| FormatException "Invalid version: 31"
        elif versionByte = 0uy && data.Length <> 66 then
            raise <| FormatException(SPrintF1 "Wrong data part length: %d (must be exactly 66 for version 0)" data.Length)
        elif data.Length < 66 then
            raise <| FormatException(SPrintF1 "Wrong data part length: %d (must be at least 66)" data.Length)
        let scanPubKeyBytes = data.[..32]
        let spendPubKeyBytes = data.[33..65]
        {
            ScanPublicKey = PubKey scanPubKeyBytes
            SpendPublicKey = PubKey spendPubKeyBytes
        }

type SilentPaymentInput =
    | InvalidInput
    | InputForSharedSecretDerivation of PubKey
    | InputJustForSpending

module SilentPayments =
    let private secp256k1 = EC.CustomNamedCurves.GetByName "secp256k1"
    
    let private scalarOrder = BigInteger("fffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364141", 16)

    let private NUMS_H_BYTES = 
        [| 0x50uy; 0x92uy; 0x9buy; 0x74uy; 0xc1uy; 0xa0uy; 0x49uy; 0x54uy; 0xb7uy; 0x8buy; 0x4buy; 0x60uy; 0x35uy; 0xe9uy; 0x7auy; 0x5euy;
           0x07uy; 0x8auy; 0x5auy; 0x0fuy; 0x28uy; 0xecuy; 0x96uy; 0xd5uy; 0x47uy; 0xbfuy; 0xeeuy; 0x9auy; 0xceuy; 0x80uy; 0x3auy; 0xc0uy; |]

    module BigInteger =
        let FromByteArrayUnsigned (bytes: array<byte>) =
            BigInteger(1, bytes)

    // see https://github.com/bitcoin/bips/blob/master/bip-0352.mediawiki#selecting-inputs
    let ConvertToSilentPaymentInput (scriptPubKey: Script) (scriptSig: array<byte>) (witness: Option<WitScript>): SilentPaymentInput =
        if scriptPubKey.IsScriptType ScriptType.P2PKH then
            // skip the first 3 op_codes and grab the 20 byte hash
            // from the scriptPubKey
            let spkHash = scriptPubKey.ToBytes().[3..3 + 20 - 1]
            let mutable result = InputJustForSpending
            for i = scriptSig.Length downto 0 do
                if i - 33 >= 0 then
                    // starting from the back, we move over the scriptSig with a 33 byte
                    // window (to match a compressed pubkey). we hash this and check if it matches
                    // the 20 byte has from the scriptPubKey. for standard scriptSigs, this will match
                    // right away because the pubkey is the last item in the scriptSig.
                    // if its a non-standard (malleated) scriptSig, we will still find the pubkey if its
                    // a compressed pubkey.
                    let pubkey_bytes = scriptSig.[i - 33..i - 1]
                    let pubkey_hash = Crypto.Hashes.Hash160 pubkey_bytes
                    if pubkey_hash.ToBytes() = spkHash then
                        let pubKey = PubKey(pubkey_bytes)
                        if pubKey.IsCompressed then
                            result <- InputForSharedSecretDerivation(pubKey)
            result
        elif scriptPubKey.IsScriptType ScriptType.P2SH then
            let redeemScript = Script scriptSig.[1..]
            if redeemScript.IsScriptType ScriptType.P2WPKH then
                let witness = witness.Value
                let pubKey = PubKey(witness.Pushes.Last())
                if pubKey.IsCompressed then
                    InputForSharedSecretDerivation(pubKey)
                else
                    InputJustForSpending
            else
                InputJustForSpending
        elif scriptPubKey.IsScriptType ScriptType.P2WPKH then
            let witness = witness.Value
            let pubKey = PubKey(witness.Pushes.Last())
            if pubKey.IsCompressed then
                InputForSharedSecretDerivation(pubKey)
            else
                InputJustForSpending
        elif scriptPubKey.IsScriptType ScriptType.Taproot then
            let witnessStack = witness.Value.Pushes |> Collections.Generic.Stack
            if witnessStack.Count >= 1 then
                if witnessStack.Count > 1 && witnessStack.Peek().[0] = 0x50uy then
                    witnessStack.Pop() |> ignore

                let internalKeyIsH =
                    if witnessStack.Count > 1 then
                        let controlBlock = witnessStack.Peek()
                        //  controlBlock is <control byte> <32 byte internal key> and 0 or more <32 byte hash>
                        let internalKey = controlBlock.[1..32]
                        internalKey = NUMS_H_BYTES
                    else
                        false
                if internalKeyIsH then
                    InputJustForSpending
                else
                    let pubKeyBytes = scriptPubKey.ToBytes().[2..]
                    let point =
                        secp256k1.Curve.DecodePoint(Array.append [| 2uy |] pubKeyBytes)
                    match PubKey.TryCreatePubKey(point.GetEncoded true) with
                    | true, pubKey -> InputForSharedSecretDerivation(pubKey)
                    | false, _ -> InputJustForSpending
            else
                InputJustForSpending
        elif scriptPubKey.IsScriptType ScriptType.P2PK 
             || scriptPubKey.IsScriptType ScriptType.MultiSig
             || scriptPubKey.IsScriptType ScriptType.P2WSH then
            InputJustForSpending
        else
            InvalidInput

    let TaggedHash (tag: string) (data: array<byte>) : array<byte> =
        let sha256 = Digests.Sha256Digest()
        
        let tag = Text.ASCIIEncoding.ASCII.GetBytes tag
        sha256.BlockUpdate(tag, 0, tag.Length)
        let tagHash = Array.zeroCreate<byte> 32
        sha256.DoFinal(tagHash, 0) |> ignore
        sha256.Reset()

        sha256.BlockUpdate(Array.append tagHash tagHash, 0, tagHash.Length * 2)
        sha256.BlockUpdate(data, 0, data.Length)

        let result = Array.zeroCreate<byte> 32
        sha256.DoFinal(result, 0) |> ignore
        result

    let GetInputHash (outpoints: List<OutPoint>) (sumInputPubKeys: EC.ECPoint) : array<byte> =
        let lowestOutpoint = outpoints |> List.map (fun outpoint -> outpoint.ToBytes()) |> List.min
        let hashInput = Array.append lowestOutpoint (sumInputPubKeys.GetEncoded true)
        TaggedHash "BIP0352/Inputs" hashInput

    let CreateOutput (privateKeys: List<Key * bool>) (outpoints: List<OutPoint>) (spAddress: SilentPaymentAddress) =
        if privateKeys.IsEmpty then
            failwith "privateKeys should not be empty"

        if outpoints.IsEmpty then
            failwith "outpoints should not be empty"

        let aSum = 
            privateKeys 
            |> List.map (
                fun (key, isTaproot) ->
                    let k = BigInteger.FromByteArrayUnsigned(key.ToBytes())
                    let yCoord = secp256k1.Curve.DecodePoint(key.PubKey.ToBytes()).YCoord.ToBigInteger()
                    if isTaproot && yCoord.Mod(BigInteger.Two) = BigInteger.One then
                        k.Negate()
                    else
                        k)
            |> List.reduce
                (fun (a: BigInteger) (b: BigInteger) -> a.Add b)

        let aSum = aSum.Mod scalarOrder

        if aSum = BigInteger.Zero then
            failwith "Input privkeys sum is zero"

        let inputHash = GetInputHash outpoints (secp256k1.G.Multiply aSum)

        let tweak = BigInteger.FromByteArrayUnsigned inputHash
        let tweakedSumSeckey = aSum.Multiply(tweak).Mod(scalarOrder)
        let ecdhSharedSecret =
            (secp256k1.Curve.DecodePoint <| spAddress.ScanPublicKey.ToBytes()).Multiply(tweakedSumSeckey).Normalize()

        let k = 0u
        let tK =
            TaggedHash
                "BIP0352/SharedSecret"
                (Array.append (ecdhSharedSecret.GetEncoded true) (BitConverter.GetBytes k))
                |> BigInteger.FromByteArrayUnsigned
        let spendPublicKey = secp256k1.Curve.DecodePoint <| spAddress.SpendPublicKey.ToBytes()
        let sharedSecret = spendPublicKey.Add(secp256k1.G.Multiply tK)

        sharedSecret.Normalize().AffineXCoord

    let GetFinalDestination (privateKey: Key) (outpoints: List<OutPoint>) (destination: string) (network: Network) : string =
        let privateKeys = 
            outpoints 
            |> List.map (fun _ -> (privateKey, false))

        let output = CreateOutput privateKeys outpoints (SilentPaymentAddress.Decode destination)
        let taprootAddress = TaprootAddress(TaprootPubKey(output.GetEncoded()), network)

        taprootAddress.ToString()
