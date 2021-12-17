#!/usr/bin/env bash
set -eux

# Set up variables needed for the regtest test

# This is needed to find lnd in PATH
export PATH=$PATH:/usr/local/go/bin:$HOME/go/bin


# Install bitcoin core
apt install -y curl tar gzip
curl -OJL https://bitcoin.org/bin/bitcoin-core-0.20.0/bitcoin-0.20.0-x86_64-linux-gnu.tar.gz
tar -C /usr/local --strip-components 1 -xzf bitcoin-0.20.0-x86_64-linux-gnu.tar.gz

# Install electrs
DEBIAN_FRONTEND="noninteractive" apt install -y clang cmake build-essential cargo
git clone https://github.com/romanz/electrs.git
pushd electrs
git checkout v0.8.10
cargo build --locked --release
cp target/release/electrs /bin/electrs
popd

# install golang
curl -OJL https://go.dev/dl/go1.17.5.linux-amd64.tar.gz
tar -C /usr/local -xzf go1.17.5.linux-amd64.tar.gz

# Install lnd
apt install -y curl tar gzip unzip make git
git clone https://github.com/lightningnetwork/lnd
pushd lnd
git checkout v0.14.1-beta
make
make install
popd

