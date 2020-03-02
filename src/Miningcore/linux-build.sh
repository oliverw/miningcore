#!/bin/bash

# the following dev-dependencies must be installed
# Ubuntu: apt-get update -y && apt-get -y install git cmake build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev

BUILDIR=${1:-../../build}
echo "Building into $BUILDIR"
dotnet publish -c Release --framework netcoreapp3.1 -o $BUILDIR
