#!/bin/bash
echo ""
echo "The following dev-dependencies must be installed"
echo "Ubuntu: apt-get install git cmake build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev"
echo ""
BUILDIR=${1:-../../build}
echo "Building into $BUILDIR"
dotnet publish -c Release --framework netcoreapp3.1 -o $BUILDIR
