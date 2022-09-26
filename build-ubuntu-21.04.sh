#!/bin/bash

# install install-dependencies
sudo apt-get update; \
  sudo apt-get -y install wget

# add dotnet repo
wget https://packages.microsoft.com/config/ubuntu/21.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# install dev-dependencies
sudo apt-get update; \
  sudo apt-get -y install dotnet-sdk-6.0 git cmake build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev libzmq5

(cd src/Miningcore && \
BUILDIR=${1:-../../build} && \
echo "Building into $BUILDIR" && \
dotnet publish -c Release --framework net6.0 -o $BUILDIR)
