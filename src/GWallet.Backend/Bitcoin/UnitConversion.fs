namespace GWallet.Backend.Bitcoin

module UnitConversion =
    let FromSatoshiToBTC(satoshis: int64): decimal =
        let satInDecimal = decimal satoshis
        let factorInDecimal = 100000000m // 8 zeros, TODO: convert to Pow function
        satInDecimal / factorInDecimal