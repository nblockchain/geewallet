# GWallet

Welcome!

GWallet is a minimalistic and pragmatist lightweight wallet for people that want to hold the most important cryptocurrencies in the same application without hassle.

| Branch   | Description                                                              | CI status                                                                                                                               |
| -------- | ------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------  |
| stable   | ETC & ETH support, console-based frontend, cold-storage support          | [![Build status badge](http://gitlab.com/knocte/gwallet/badges/stable/build.svg)](https://gitlab.com/knocte/gwallet/commits/stable)     |
| master   | +BTC support (SegWit & RBF support); soon: LTC                           | [![Build status badge](http://gitlab.com/knocte/gwallet/badges/master/build.svg)](https://gitlab.com/knocte/gwallet/commits/master)     |
| frontend | +Xamarin.Forms frontends in progress (now: Android, iOS; soon: gtk, UWP) | [![Build status badge](http://gitlab.com/knocte/gwallet/badges/frontend/build.svg)](https://gitlab.com/knocte/gwallet/commits/frontend) |


## Principles

Given GWallet can handle multiple cryptocurrencies, its UX needs to be as generic as possible to accomodate them, and should only contain minimal currency-specific features. The best example to describe this approach is the absence of change addresses concept; given that this concept doesn't exist in Ethereum-based cryptocurrencies, and it doesn't achieve much privacy anyway in the Bitcoin-based ones, GWallet approach will be to wait for other technologies to be adopted mainstream first that could help on this endeavour, such as TumbleBit or ConfidentialTransactions.

This is also because we prioritize security & convenience over privacy. For example GWallet has cold-storage support (you can run it in off-line mode and import/export transactions in JSON files) but still hasn't implemented TLS for communication with Electrum servers (this only hinders privacy but doesn't pose any security risk; but given that in the Ethereum world the users don't expect such high levels of privacy, due to the lack of HD wallets or change addresses for example, we consider the common denominator between currencies to be our standard).

GWallet will always be standing in the shoulders of giants, which means we have a complete commitment to opensource as way of evolving the product and achieving the maximum level of security/auditability; unlike other multi-currency wallets (cough... Jaxx ...cough).


## Roadmap

This list is the (intended) order of preference for new features:

- Xamarin.Forms frontends (in progress, see the 'frontend' branch)...
- LTC support.
- Fee selection for custom priority.
- Multi-sig support.
- Payment channels support.
- Lightning support (upgrading to NBitcoin 4.0.0.12 to be protected from malleability).
- ETH/ETC raiden (state channels) support.
- Use bits instead of BTC as default unit.
(See: https://www.reddit.com/r/Bitcoin/comments/7hsq6m/symbol_for_a_bit_0000001btc/ )
- MimbleWimble support?
- Threshold signatures.
- Tumblebit support?
- Decentralized currency exchange?
- Decentralized naming resolution? (BlockStack vs ENS?)


## Anti-roadmap

Things we will never develop (if you want them, feel free to fork us):

- ZCash support (I don't like the trusted setup, if you want privacy just wait for MimbleWimble).
- Ripple/Stellar/OneCoin support (they're all a scam).
- Dash/Monero (I prefer other privacy solutions to the way these currencies have tried to deal with it)


# How to compile/install/use?

The recommended way is to install the software system wide, like this:

```
./configure.sh --prefix=/usr
make
sudo make install
```

After that you can call `gwallet` directly.


## Feedback

If you want to accelerate development/maintenance, please donate at... TBD.
