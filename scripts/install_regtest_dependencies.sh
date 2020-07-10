#!/usr/bin/env bash
set -eux

# install bitcoin core
apt install -y curl tar gzip
curl -OJL https://bitcoin.org/bin/bitcoin-core-0.20.0/bitcoin-0.20.0-x86_64-linux-gnu.tar.gz
tar -C /usr/local --strip-components 1 -xzvf bitcoin-0.20.0-x86_64-linux-gnu.tar.gz

# install electrumx
apt install -y curl unzip python3-pip
curl -L https://github.com/spesmilo/electrumx/archive/1.15.0.zip -o electrumx-1.15.0.zip
unzip electrumx-1.15.0.zip
pip3 install ./electrumx-1.15.0

# install lnd
apt install -y curl tar gzip unzip make
curl -OJL https://golang.org/dl/go1.14.4.linux-amd64.tar.gz
tar -C /usr/local -xzvf go1.14.4.linux-amd64.tar.gz
export PATH=$PATH:/usr/local/go/bin
curl -L https://github.com/lightningnetwork/lnd/archive/v0.10.3-beta.zip -o lnd-0.10.3-beta.zip
unzip lnd-0.10.3-beta.zip
pushd lnd-0.10.3-beta
make
make install
popd

