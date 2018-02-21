namespace GWallet.Backend.Bitcoin

open System

//FIXME: use NBitcoin conversion APIs instead of these? or use F#'s units of measure
module UnitConversion =
    let private HOW_MANY_SATOSHIS_ARE_THERE_IN_ONE_BTC = 100000000m

    let FromSatoshiToBtc (satoshis: int64): decimal =
        let satInDecimal = Convert.ToDecimal satoshis
        // 8 zeros, TODO: convert to Pow function?
        let factorInDecimal = HOW_MANY_SATOSHIS_ARE_THERE_IN_ONE_BTC
        satInDecimal / factorInDecimal

    let FromBtcToSatoshis (btcAmount: decimal) =
        Convert.ToInt64(btcAmount * HOW_MANY_SATOSHIS_ARE_THERE_IN_ONE_BTC)
