#!/usr/bin/env bash
set -eux

# Set up variables needed for the regtest test

# This is needed to find lnd in PATH
export PATH=$PATH:/usr/local/go/bin:$HOME/go/bin


# Install bitcoin core
sudo apt install -y curl tar gzip
curl -OJL https://bitcoin.org/bin/bitcoin-core-0.20.0/bitcoin-0.20.0-x86_64-linux-gnu.tar.gz
sudo tar -C /usr/local --strip-components 1 -xzf bitcoin-0.20.0-x86_64-linux-gnu.tar.gz

# Install electrs
sudo DEBIAN_FRONTEND="noninteractive" apt install -y clang cmake build-essential cargo
git clone https://github.com/romanz/electrs.git
cd electrs
git checkout v0.8.9
cargo build --locked --release
sudo cp target/release/electrs /bin/electrs
cd ..

# Install lnd
sudo apt install -y curl tar gzip unzip make git golang
git clone https://github.com/lightningnetwork/lnd
cd lnd
git checkout v0.10.3-beta
make
make install
cd ..

