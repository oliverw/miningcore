#!/bin/bash
echo ""
echo "The following dev-dependencies must be installed"
echo "Ubuntu: apt-get install git cmake build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev libzmq5"
echo ""
sudo apt-get install git cmake build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev libzmq5

echo .
echo "Installing dotnet SDK core 2.1"
sudo apt-get update; \
  sudo apt-get install -y apt-transport-https && \
  sudo apt-get update && \
  sudo apt-get install -y dotnet-sdk-2.1

echo ""
echo "Installing dotnet SDK core 3.1"
sudo apt-get update; \
  sudo apt-get install -y apt-transport-https && \
  sudo apt-get update && \
  sudo apt-get install -y dotnet-sdk-3.1

BUILDIR=${1:-../../build}
echo "Building into $BUILDIR"
dotnet publish -c Release --framework netcoreapp2.1 --runtime linux-x64 --self-contained true -o $BUILDIR
