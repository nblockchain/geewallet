# <img src="https://github.com/knocte/geewallet/raw/master/logo.png" width="50" /> GWallet

Welcome!

GWallet is a minimalistic and pragmatist crossplatform lightweight opensource brainwallet for people that want to hold the most important cryptocurrencies in the same application with ease and peace of mind.

[![Licence](https://img.shields.io/github/license/knocte/gwallet.svg)](https://github.com/knocte/gwallet/blob/master/LICENCE.txt)

| Branch            | Description                                                            | CI status (build & test suite)                                                                                                                                                |
| ----------------- | ---------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| stable (v0.2.x)   | Console frontend, currencies: ETC&ETH+DAI(ERC20), BTC&LTC (SegWit+RBF) | Linux: [![Linux CI pipeline status badge](http://gitlab.com/knocte/geewallet/badges/stable/build.svg)](https://gitlab.com/knocte/geewallet/commits/stable)      |
| master (v0.3.x)   | main branch where ongoing development takes place (unstable)           | Linux: [![Linux CI pipeline status badge](http://gitlab.com/knocte/geewallet/badges/master/build.svg)](https://gitlab.com/knocte/geewallet/commits/master) <br/>macOS: [![macOS CI pipeline status badge](https://dev.azure.com/diginex/geewallet/_apis/build/status/master-macOS)](https://dev.azure.com/diginex/geewallet/_build/latest?definitionId=7) <br/>Windows: [![Windows CI pipeline status badge](https://dev.azure.com/diginex/geewallet/_apis/build/status/master-Windows)](https://dev.azure.com/diginex/geewallet/_build/latest?definitionId=1) |
| frontend (v0.4.x) | + Xamarin.Forms frontends (Android & iOS & Gtk & macOS & UWP)          | Linux: [![Linux CI pipeline status badge](http://gitlab.com/knocte/geewallet/badges/frontend/build.svg)](https://gitlab.com/knocte/geewallet/commits/frontend)  |

[![Balances mobile-page screenshot](https://raw.githubusercontent.com/knocte/gwallet/master/img/screenshots/maciosandroid-balances.png)](https://raw.githubusercontent.com/knocte/gwallet/master/img/screenshots/maciosandroid-balances.png)

## Principles

This is a wallet that prioritizes convenience & security over privacy. Examples:

1. Convenience: it is a lightweight/thin wallet (you don't need to download whole blockchains to use it, unlike with fullnode wallets).
2. Convenience over privacy (I): it's a wallet that can handle multiple cryptocurrencies, so its UX needs to be as generic as possible to accomodate them, therefore only contains minimal currency-specific features. For example, given that the concept of "change-addresses" doesn't exist in the Ethereum world (a concept initially thought to help privacy in the bitcoin world, but which doesn't achieve much of it in the end), then it is not used even when sending bitcoin, to avoid cluttering the UI/UX with currency-specific features/complexities. We will still be investigating the support of more robust privacy features such as the ones provided by TumbleBit or ConfidentialTransactions.
3. Convenience over privacy (II): servers from other wallets' infrastructure is reused (e.g. Electrum's Stratum protocol), however TLS communication is still unsupported (this only hinders privacy but doesn't pose any security risk).
4. Security (I): it's a desktop/mobile wallet, not an online/web wallet like others (e.g. web wallets are easy targets: https://twitter.com/myetherwallet/status/988830652526092288 ).
5. Security (II): it has cold-storage support (you can run it in off-line mode and import/export transactions in JSON files), but not hardware wallet support. Remember, cold storage is not the same as 'hardware wallet'. This is a software wallet, but which works in air-gapped devices (computers/smartphones) thanks to its cold-storage support, which means that it's safer than hardware wallets (after all, bugs and security issues are constantly being found on hardware wallets, e.g.: https://saleemrashid.com/2018/03/20/breaking-ledger-security-model/).
6. Convenience (II): there are no pre-generated seeds, this is a brainwallet that uses your passphrase as a seed phrase, so that you don't need to keep backups anymore (and if you have any doubt about the security of this, understand that a hacker will always want to try to solve the WarpWallet challenge rather than target you directly).

In the development side of things, we advocate for simplicity:
1. We will always be standing in the shoulders of giants, which means that we should not reinvent the wheel, thus we have a complete commitment to opensource as way of evolving the product and achieving the maximum level of security/auditability; unlike other multi-currency wallets (cough... Jaxx ...cough).
2. We will try to only add new features to the UX/UI that can be supported by all currencies that we support, and we will prioritize new features (Layer2: micropayments) over support for new currencies (no shitcoins thanks).
3. Thanks to our usage of Xamarin.Forms toolkit, our frontends are based on a single codebase, instead of having to repeat logic for each platform.

## Roadmap

This list is the (intended) order of preference for new features:

- Xamarin.Forms frontends (in progress, see the 'frontend' branch)...
- Support for payment-channels & state-channels (in BTC/LTC via lightning, and in ETH/ETC/DAI via Raiden)
- snap packaging.
- Decentralized currency exchange? (e.g. eth2dai.com)
- NFC support.
- Tizen frontend (no QR scanning due to missing camera in most Tizen watches, but could use NFC).
- Paranoid-build mode (using git submodules or [local nugets](https://github.com/mono/mono-addins/issues/73#issuecomment-389343246), instead of binary nuget deps), depending on [this RFE](https://github.com/dotnet/sdk/issues/1151) or using any workaround mentioned there.
- Passwordless login infrastructure (see https://twitter.com/VitalikButerin/status/1118405098449903617 ).
- flatpak packaging.
- In mobile, allow usage when camera permissions have not been granted, by letting the user redirect him to his camera app and take a picture (see https://youtu.be/k1Ssz1dvcpk?t=63).
- Use of 'bits' instead of BTC as default unit.
(See: https://www.reddit.com/r/Bitcoin/comments/7hsq6m/symbol_for_a_bit_0000001btc/ )
- MimbleWimble(Grin) support.
- Threshold signatures.
- Use deniable encryption to allow for a duress password/passphrase/pin.
- ETH gas station (to pay for token transactions with token value instead of ETH).
- Fee selection for custom priority.
- Multi-sig support?
- Crosschain atomic swaps (via [comit network](https://github.com/comit-network/comit-rs)? more info [here](https://blog.coblox.tech/2018/06/23/connect-all-the-blockchains.html) and [here](https://blog.coblox.tech/2018/12/12/erc20-lightning-and-COMIT.html)).
- Decentralized naming resolution? (BNS/ENS/OpenCAP/...)
- Tumblebit support?
- Consider [Vitalik's 1wei wallet-funding idea](https://twitter.com/VitalikButerin/status/1103997378967810048) in case the community adopts it.


## Anti-roadmap

Things we will never develop (if you want them, feel free to fork us):

- ZCash/Dash/Monero support (I don't like the trusted setup of the first, plus the others use substandard
privacy solutions which in my opinion have been all surpassed by MimbleWimble/Grin).
- Ripple/Stellar/OneCoin support (they're all a scam).
- BCash (as it's less evolved, technically speaking; I don't want to deal with transaction malleability
or lack of Layer2 scaling).


## How to compile/install/use?

The recommended way is to install the software system wide, like this:

```
./configure.sh --prefix=/usr
make
sudo make install
```

After that you can call `gwallet` directly.


## Thanks

Special thanks to all the [contributors](https://gitlab.com/knocte/geewallet/graphs/frontend) (we recently surpassed 10! if you count the contributions that are in review at the moment). Without forgetting as well the amazing developers that contribute(d) to the great opensource libraries that this project uses; some examples:

- @juanfranblanco: Nethereum
- @nicolasdorier, @joemphilips: NBitcoin
- @redth, @EBrown8534, @mierzynskim: ZXing.Net.Mobile, ZXing.Net.Xamarin
- JsonRpcSharp: @ardave, @martz2804, @jerry40, @mierzynskim, @winstongubantes and again @juanfranblanco
- ...and all the Xamarin/Mono/.NetCore community in general, of course

If you want to become part of this distributed team of brave disruptarians, check our [CONTRIBUTING guideline](CONTRIBUTING.md) first, and start coding!


## Feedback

If you want to accelerate development/maintenance, create an issue and pledge funds with [gitcoin](http://gitcoin.co).
