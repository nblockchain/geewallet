namespace GWallet.Backend.UtxoCoin.Lightning.Watcher

type TowerApiError = 
    | UnsupportedCurrency

type TowerApiResponseOrError<'T> =
    | TowerApiResponse of 'T
    | TowerApiError of TowerApiError
