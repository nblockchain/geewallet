#!/usr/bin/env bash
set -eux

# Set up variables needed for the regtest test

# This is needed to find lnd in PATH
export PATH=$PATH:/usr/local/go/bin:$HOME/go/bin
# This is needed to convince electrum to run on CI as root
export ALLOW_ROOT=1


# Install bitcoin core
sudo apt install -y curl tar gzip
curl -OJL https://bitcoin.org/bin/bitcoin-core-0.20.0/bitcoin-0.20.0-x86_64-linux-gnu.tar.gz
sudo tar -C /usr/local --strip-components 1 -xzf bitcoin-0.20.0-x86_64-linux-gnu.tar.gz

# Install electrumx
sudo apt install -y curl unzip python3-pip
curl -L https://github.com/spesmilo/electrumx/archive/1.15.0.zip -o electrumx-1.15.0.zip
unzip electrumx-1.15.0.zip
pip3 install ./electrumx-1.15.0

# Install lnd
sudo apt install -y curl tar gzip unzip make git
curl -OJL https://golang.org/dl/go1.14.4.linux-amd64.tar.gz
sudo tar -C /usr/local -xzf go1.14.4.linux-amd64.tar.gz
git clone https://github.com/lightningnetwork/lnd
cd lnd
git checkout v0.10.3-beta
make
make install
cd ..

