# <img src="https://github.com/diginex/geewallet/raw/master/logo.png" width="50" /> GWallet

Welcome!

GWallet is a minimalistic and pragmatist crossplatform lightweight opensource brainwallet for people that want to hold the most important cryptocurrencies in the same application with ease and peace of mind.

[![Licence](https://img.shields.io/github/license/diginex/geewallet.svg)](https://github.com/diginex/geewallet/blob/master/LICENCE.txt)

| Branch            | Description                                                            | CI status                                                                                                                                                                    |
| ----------------- | ---------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| stable (v0.2.x)   | Console frontend, currencies: ETC&ETH+DAI(ERC20), BTC&LTC (SegWit+RBF) | [![Linux CI pipeline status badge](http://gitlab.com/DiginexGlobal/geewallet/badges/stable/build.svg)](https://gitlab.com/DiginexGlobal/geewallet/commits/stable)(Linux)     |
| master (v0.3.x)   | main branch where ongoing development takes place (unstable)           | [![Linux CI pipeline status badge](http://gitlab.com/DiginexGlobal/geewallet/badges/master/build.svg)](https://gitlab.com/DiginexGlobal/geewallet/commits/master)(Linux) [![Windows CI pipeline status badge](https://dev.azure.com/diginex/geewallet/_apis/build/status/geewallet-master-build-and-test)](https://dev.azure.com/diginex/geewallet/_build/latest?definitionId=1)(Windows) |
| frontend (v0.4.x) | + Xamarin.Forms frontends (Android & iOS & Gtk & macOS & UWP)          | [![Linux CI pipeline status badge](http://gitlab.com/DiginexGlobal/geewallet/badges/frontend/build.svg)](https://gitlab.com/DiginexGlobal/geewallet/commits/frontend)(Linux) |

[![Balances mobile-page screenshot](https://raw.githubusercontent.com/diginex/geewallet/master/img/screenshots/mobile-balances.png)](https://raw.githubusercontent.com/diginex/geewallet/master/img/screenshots/mobile-balances.png)

## Principles

GWallet is a wallet that prioritizes convenience & security over privacy. Examples:

1. Convenience: it is a lightweight wallet (you don't need to download whole blockchains to use it, unlike with Bitcoin Core's wallet).
2. Convenience over privacy (I): it's a wallet that handle multiple cryptocurrencies, so its UX needs to be as generic as possible to accomodate them, therefore only contains minimal currency-specific features. For example, given that the concept of "change-addresses" doesn't exist in the Ethereum world (a concept initially thought to help privacy in the bitcoin world, but which doesn't achieve much of it in the end), then it is not used by GWallet even when sending bitcoin, to avoid cluttering the UI/UX with currency-specific features/complexities. We will still be investigating the support of more robust privacy features such as the ones provided by TumbleBit or ConfidentialTransactions.
3. Convenience over privacy (II): servers from other wallets' infrastructure is reused (e.g. Electrum's Stratum protocol), however TLS communication is still unsupported (this only hinders privacy but doesn't pose any security risk).
4. Security (I): GWallet is a desktop/mobile wallet, not an online/web wallet like others (e.g. web wallets are easy targets: https://twitter.com/myetherwallet/status/988830652526092288 ).
5. Security (II): GWallet has cold-storage support (you can run it in off-line mode and import/export transactions in JSON files), but not hardware wallet support. Remember, cold storage is not the same as 'hardware wallet'. GWallet is a software wallet, but which works in air-gapped devices (computers/smartphones) thanks to its cold-storage support, which means that it's safer than hardware wallets (after all, bugs and security issues are constantly being found on hardware wallets, e.g.: https://saleemrashid.com/2018/03/20/breaking-ledger-security-model/).
6. Convenience (II): there are no pre-generated seeds, geewallet is a brainwallet that uses your passphrase as a seed phrase, so that you don't need to keep backups anymore (and if you have any doubt about the security of this, understand that a hacker will always want to try to solve the WarpWallet challenge rather than target you directly).

In the development side of things, we advocate for simplicity:
1. We will always be standing in the shoulders of giants, which means that we should not reinvent the wheel, thus we have a complete commitment to opensource as way of evolving the product and achieving the maximum level of security/auditability; unlike other multi-currency wallets (cough... Jaxx ...cough).
2. We will try to only add new features to the UX/UI that can be supported by all currencies that we support, and we will prioritize new features (Layer2: micropayments) over support for new currencies (no shitcoins thanks).
3. Thanks to our usage of Xamarin.Forms toolkit, our frontends are based on a single codebase, instead of having to repeat logic for each platform.

## Roadmap

This list is the (intended) order of preference for new features:

- Xamarin.Forms frontends (in progress, see the 'frontend' branch)...
- Support for payment-channels & state-channels (in BTC/LTC via lightning, and in ETH/ETC/DAI via Raiden)
- macOS CI support via AzureDevOps pipelines.
- snap packaging.
- Paranoid-build mode (using git submodules instead of nuget deps), depending on this RFE (https://github.com/dotnet/sdk/issues/1151) or using any workaround mentioned there.
- flatpak packaging.
- Use of 'bits' instead of BTC as default unit.
(See: https://www.reddit.com/r/Bitcoin/comments/7hsq6m/symbol_for_a_bit_0000001btc/ )
- MimbleWimble(Grin) support.
- Threshold signatures.
- ETH gas station (to pay for token transactions with token value instead of ETH).
- Fee selection for custom priority.
- Multi-sig support.
- Decentralized naming resolution? (BNS/ENS/OpenCAP/...)
- Decentralized currency exchange? or crosschain atomic swaps?
- Tumblebit support?


## Dev roadmap

(Only intelligible if you're a GWallet developer):
- Switch to use https://github.com/madelson/MedallionShell in Infra.fs (we might want to use paket instead of nuget for this, as it's friendlier to .fsx scripts, see https://cockneycoder.wordpress.com/2017/08/07/getting-started-with-paket-part-1/, or wait for https://github.com/Microsoft/visualfsharp/pull/5850).
- Refactor bitcoin support to use NBitcoin's TransactionBuilder.
- Study the need for ConfigureAwait(false) in the backend (or similar & easier approaches such as https://blogs.msdn.microsoft.com/benwilli/2017/02/09/an-alternative-to-configureawaitfalse-everywhere/ or https://github.com/Fody/ConfigureAwait ).

## Anti-roadmap

Things that are not currently on our roadmap:

- ZCash/Dash/Monero support (I don't like the trusted setup of the first, plus the others use substandard
privacy solutions which in my opinion will all be surpassed by MimbleWimble).
- BCash (as it's less evolved, technically speaking; I don't want to deal with transaction malleability
or lack of Layer2 scaling).

# How to compile/install/use?

The recommended way is to install the software system wide, like this:

```
./configure.sh --prefix=/usr
make
sudo make install
```

After that you can call `gwallet` directly.


## Feedback

If you want to accelerate development/maintenance, create an issue and pledge funds with [gitcoin](http://gitcoin.co).
Alternatively, if you want to hire expertise around blockchain development or adapt this project
to your needs, see the next section.


## About Diginex

![Diginex Logo](https://www.diginex.com/wp-content/uploads/2018/09/diginex_chain_logo_-01-copy.png)

Diginex develops and implements blockchain technologies to transform businesses and enrich society. At the core of Diginex is our people. We are a blend of financial service professionals, passionate blockchain technologists and experienced project managers. We work with corporates, institutions & governments to create solutions that build trust and increase efficiency.
