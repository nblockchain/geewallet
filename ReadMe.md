# <img src="https://github.com/nblockchain/geewallet/raw/master/logo.png" width="50" /> geewallet

Welcome!

geewallet is a minimalistic and pragmatist crossplatform lightweight opensource brainwallet for people that want to hold the most important cryptocurrencies in the same application with ease and peace of mind.

[![Licence](https://img.shields.io/github/license/nblockchain/geewallet.svg)](https://github.com/nblockchain/geewallet/blob/master/LICENCE.txt)

| Branch            | Description                                                            | Build & Test status                                                                         |
| ----------------- | ---------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| stable (v0.4.x)   | Console frontend (Linux snap pkg name: gwallet)                        | [![GitHubCI pipeline status badge](https://github.com/nblockchain/geewallet/workflows/CI/badge.svg?branch=stable)](https://github.com/nblockchain/geewallet/commits/stable) |
| master (v0.5.x)   | Xamarin.Forms frontend: Android, iOS, Linux/GTK (pkg name: geewallet)  | [![GitHubCI pipeline status badge](https://github.com/nblockchain/geewallet/workflows/CI/badge.svg?branch=master)](https://github.com/nblockchain/geewallet/commits/master) |

[![Balances mobile-page screenshot](https://raw.githubusercontent.com/nblockchain/geewallet/master/img/screenshots/maciosandroid-balances.png)](https://raw.githubusercontent.com/nblockchain/geewallet/master/img/screenshots/maciosandroid-balances.png)

## Features

Comparing our product to other wallets in terms of features, we would want to highlight the following:

| Wallet\Feature | Multi-currency | Opensource | Cold Storage | Brain seed | Truly crossplatform* |
| -------------- | -------------- | ---------- | ------------ | ---------- | -------------------- |
| Copay/Bitpay   | No             | **Yes**    | No           | No         | **Yes**              |
| Electrum       | No             | **Yes**    | **Yes**      | No         | No                   |
| Bread          | **Yes**        | **Yes**    | **Yes**      | No         | **Yes**              |
| Samourai       | No             | **Yes**    | **Yes**      | No         | No                   |
| Wasabi         | No             | **Yes**    | No           | No         | No                   |
| Mycelium       | No             | **Yes**    | **Yes**      | No         | No                   |
| Jaxx           | **Yes**        | No         | No           | No         | **Yes**              |
| Coinomi        | **Yes**        | No         | No           | No         | **Yes**              |
| ParitySigner   | No             | **Yes**    | **Yes**      | No         | **Yes**              |
| imToken        | **Yes**        | No         | **Yes**      | No         | No                   |
| status.im      | No             | **Yes**    | No           | No         | **Yes**              |
| Edge           | **Yes**        | **Yes**?   | No           | No         | No                   |
| WarpWallet     | No             | **Yes**    | No           | **Yes**    | No                   |
| ![](https://raw.githubusercontent.com/nblockchain/geewallet/master/img/markdown/geewallet.svg?sanitize=true) | **YES** | **YES** | **YES** | **YES** | **YES** |

*=With truly crossplatform we mean Mobile (both Android & iPhone) & Desktop (main OSs: Linux, macOS & Windows)

As you can see, geewallet is a good mixup of good features, which others never manage to get together in the same app. I should add to this that geewallet's future maintainability is very high due to:
- Using a functional programming language.
- Having lots of automated tests.
- Employing a single code base for all frontends, requiring much less manpower and specific expertise.


## Principles

This is a wallet that prioritizes convenience & security over privacy. Examples:

1. Convenience (I): it is a lightweight/thin wallet (you don't need to download whole blockchains to use it, unlike with fullnode wallets).
2. Convenience (II): there are no pre-generated seeds, this is a brainwallet that uses your passphrase as a seed phrase, so that you don't need to keep backups anymore (and if you have any doubt about the security of this, understand that a hacker will always want to try to solve the WarpWallet challenge rather than target you directly).
3. Security (I): it's a desktop/mobile wallet, not an online/web wallet like others (e.g. web wallets are easy targets: https://twitter.com/myetherwallet/status/988830652526092288 ).
4. Security (II): it has cold-storage support (you can run it in off-line mode and import/export transactions in JSON files), but not hardware wallet support. Remember, cold storage is not the same as 'hardware wallet'. This is a software wallet, but which works in air-gapped devices (computers/smartphones) thanks to its cold-storage support, which means that it's safer than hardware wallets (after all, bugs and security issues are constantly being found on hardware wallets, e.g.: https://saleemrashid.com/2018/03/20/breaking-ledger-security-model/).
5. Convenience over privacy: it's a wallet that can handle multiple cryptocurrencies, so its UX needs to be as generic as possible to accomodate them, therefore only contains minimal currency-specific features. For example, given that the concept of "change-addresses" doesn't exist in the Ethereum world (a concept initially thought to help privacy in the bitcoin world, but which doesn't achieve much of it in the end), then it is not used even when sending bitcoin, to avoid cluttering the UI/UX with currency-specific features/complexities (e.g. see https://twitter.com/NicolasDorier/status/1195181085702774784 ). We will still be investigating the support of more robust privacy features such as the ones provided by TumbleBit or ConfidentialTransactions.

In the development side of things, we advocate for simplicity:
1. We will always be standing in the shoulders of giants, which means that we should not reinvent the wheel, thus we have a complete commitment to opensource as way of evolving the product and achieving the maximum level of security/auditability; unlike other multi-currency wallets (cough... Jaxx ...cough).
2. We will try to only add new features to the UX/UI that can be supported by all currencies that we support, and we will prioritize new features (Layer2: micropayments) over support for new currencies (no shitcoins thanks).
3. Thanks to our usage of Xamarin.Forms toolkit, our frontends are based on a single codebase, instead of having to repeat logic for each platform.


## Roadmap

This list is the (intended) order of preference for new features:

- Migration from Xamarin.Forms to MAUI (in progress, see the PR#199 or its successor).
- Switch from SegWit to native-SegWit (Bech32): PR#211.
- Support for payment-channels & state-channels (in BTC/LTC via lightning, see 'lightning' branch; and in ETH/ETC/DAI via Connext?).
- Explore better stablecoin approach than L1-DAI because DAI is not 100% decentralized (e.g. LUSD better?) and because L1 is too high fees (e.g. sDAI better? see https://twitter.com/koeppelmann/status/1736766570825654630 ).
- Automatic reminders for Seed/password checks to make sure user has not forgotten them (see https://twitter.com/takinbo/status/1201529679519330305 ).
- NFC support.
- WearOS/WatchOS frontend for Samsung&Google&Apple watches: no QR scanning due to missing camera in most Tizen watches, but could use NFC.
- Passwordless login infrastructure (e.g. https://www.reddit.com/r/ethereum/comments/7hn3fq/using_your_blockchain_identity_for_passwordless/ ).
- flatpak packaging.
- In mobile, allow usage when camera permissions have not been granted, by letting the user redirect him to his camera app and take a picture (see https://youtu.be/k1Ssz1dvcpk?t=63).
- Use of 'bits' instead of BTC as default unit (see https://www.reddit.com/r/Bitcoin/comments/sdhtz0/this_woman_has_resparked_the_sats_vs_bits_debate/ and https://twitter.com/ericyakes/status/1708876421081465078 ).
(See: https://www.reddit.com/r/Bitcoin/comments/7hsq6m/symbol_for_a_bit_0000001btc/ or in case we end up using sats, this "1 over 2" idea is interesting: 
https://twitter.com/Parman_Public/status/1603698639947599873 )
- MimbleWimble(LTC) support (WIP, see https://github.com/nblockchain/NLitecoin/pull/2 ).
- Threshold signatures (or asynchronous ones? see https://twitter.com/real_or_random/status/1523964246945636352 ).
- Use deniable encryption to allow for a duress password/passphrase/pin.
- Multi-sig support?
- Fee selection for custom priority (so that our RBF support becomes actually useful).
- ETH gas station (to pay for token transactions with token value instead of ETH).
- Crosschain atomic swaps (via [comit network](https://github.com/comit-network/comit-rs)? more info [here](https://blog.coblox.tech/2018/06/23/connect-all-the-blockchains.html) and [here](https://blog.coblox.tech/2018/12/12/erc20-lightning-and-COMIT.html)).
- Decentralized naming resolution? (BNS/ENS/OpenCAP/...), ideally adding this feature in the browser-extension frontend (in the same way MetaMask enables ENS). As an example, this wallet seems to build on BNS: 
https://twitter.com/markymark/status/1625458698243166210 , https://wallet.hiro.so/ , https://github.com/hirosystems/stacks-wallet-web
- identicon (e.g. https://jdenticon.com/) support to identify recipients/channels/invoices
- TapRoot support?
- Consider [Vitalik's 1wei wallet-funding idea](https://twitter.com/VitalikButerin/status/1103997378967810048) in case the community adopts it.
- UI testing with Selenium+UnoPlatform (see https://www.prnewswire.com/news-releases/uno-platform-announces-version-2-0-of-cross-platform-development-platform-300921202.html).
- Blazor frontend (for browser extension?), which could enable Playwright/Selenium testing.
- Support for Stratum protocol over SSL.
- Decentralized currency exchange (e.g. thorchain?).


## Anti-roadmap

Things we will never develop (if you want them, feel free to fork us):

- ZCash/Dash/Monero support (I don't like the trusted setup of the first, plus the others use substandard
privacy solutions which in my opinion have been all surpassed by MimbleWimble/Grin).
- Ripple/Stellar/OneCoin support (they're all a scam).
- BCash (as it's less evolved, technically speaking; I don't want to deal with transaction malleability
or lack of Layer2 scaling).


## How to compile/install/use?

The easiest way to use for non-technical people is to install it from the Android AppStore:
[![Get it on Google Play](https://play.google.com/intl/en_us/badges/static/images/badges/en_badge_web_generic.png)](https://play.google.com/store/apps/details?id=com.geewallet.android)

Or if you use Linux, from the Snap Store:
[![Get it from the Snap Store](https://snapcraft.io/static/images/badges/en/snap-store-black.svg)](https://snapcraft.io/geewallet)

To install via the command-line in (Ubuntu) Linux, do it this way:

```
snap install geewallet
```

(For the command-line client, use the name `gwallet` instead of `geewallet`.)


Other platforms: coming soon.

If you're an advanced user, you could clone it and compile it yourself this way (in macOS or Linux):

```
./configure.sh --prefix=/usr/local
make
sudo make install
```

Or this way if you're on Windows:

```
configure.bat
make.bat
```


## Thanks

Special thanks to all the [contributors](https://github.com/nblockchain/geewallet/graphs/contributors) (we recently surpassed 10! if you count the contributions that are in review at the moment). Without forgetting as well the amazing developers that contribute(d) to the great opensource libraries that this project uses; some examples:

- @juanfranblanco: Nethereum
- @nicolasdorier, @joemphilips: NBitcoin
- @redth, @EBrown8534, @mierzynskim: ZXing.Net.Mobile, ZXing.Net.Xamarin
- Xamarin.Forms: @mfkl, @stanbav, @AndreiMisiukevich, @melimion, @z3ut
- JsonRpcSharp: @lukethenuke, @mfkl, @jerry40, @mierzynskim, @winstongubantes and again @juanfranblanco
- ...and all the Xamarin/Mono/.NetCore community in general, of course

If you want to become part of this distributed team of brave disruptarians, check our [CONTRIBUTING guideline](CONTRIBUTING.md) first, and start coding!


## Feedback

If you want to accelerate development/maintenance, create an issue and pledge funds with [gitcoin](http://gitcoin.co).
