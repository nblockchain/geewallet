# GWallet

Welcome!

GWallet is a minimalistic and pragmatist lightweight wallet for people that want to hold the most important cryptocurrencies in the same application without hassle.

## Principles

Given GWallet can handle multiple cryptocurrencies, its UX needs to be as generic as possible to accomodate them, and should only contain minimal currency-specific features. The best example to describe this approach is the absence of change addresses concept; given that this concept doesn't exist in Ethereum-based cryptocurrencies, and it doesn't achieve much privacy anyway in the Bitcoin-based ones, GWallet approach will be to wait for other technologies to be adopted mainstream first that could help on this endeavour, such as TumbleBit or ConfidentialTransactions.

GWallet will always be standing in the shoulders of giants, which means we have a complete commitment to opensource as way of evolving the product and achieving the maximum level of security/auditability; unlike other multi-currency wallets (cough... Jaxx ...cough).


## Roadmap

This list is the (intended) order of preference for new features:

- LTC support.
- RBF support.
- Fee selection for custom priority.
- Multi-sig support.
- LTC payment channels support.
- BTC tumblebit support.
- Xamarin.Forms frontend.
- ETH raiden (state channels) support.
- BTC lightning support (if SegWit activates).
- Threshold signatures.
- MimbleWimble support?
- Decentralized currency exchange?
- Decentralized naming resolution? (BlockStack vs ENS?)


## Anti-roadmap

Things we will never develop (if you want them, feel free to fork us):

- ZCash support (I don't like the trusted setup, if you want privacy just wait for MimbleWimble).
- Ripple/Stellar/OneCoin support (they're all a scam).
- Dash/Monero (I prefer other privacy solutions to the way these currencies have tried to deal with it)
- ICOs.
- Tokens (maybe I'll make an exception to BAT).


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
