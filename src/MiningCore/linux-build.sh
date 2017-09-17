#!/bin/bash

# the following dev-dependencies must be installed
# Ubuntu: apt-get update -y && apt-get -y install git cmake build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev

# publish
mkdir -p ../../build
dotnet publish -c Release --framework netcoreapp2.0 -o ../../build

# build libcryptonote
(cd ../Native/libcryptonote && make && cp libcryptonote.so ../../../build && make clean)

# build libmultihash
(cd ../Native/libmultihash && make && cp libmultihash.so ../../../build && make clean)
