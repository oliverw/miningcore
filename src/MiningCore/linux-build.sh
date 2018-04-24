#!/bin/bash

# the following dev-dependencies must be installed
# Ubuntu: apt-get update -y && apt-get -y install git cmake build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev

BUILDIR=${1:-../../build}

echo "Building into $BUILDIR"

# publish
mkdir -p $BUILDIR
dotnet publish -c Release --framework netcoreapp2.0 -o $BUILDIR

# build libcryptonote
(cd ../Native/libcryptonote && make)
cp ../Native/libcryptonote/libcryptonote.so $BUILDIR
(cd ../Native/libcryptonote && make clean)

# build libmultihash
(cd ../Native/libmultihash && make)
cp ../Native/libmultihash/libmultihash.so $BUILDIR
(cd ../Native/libmultihash && make clean)

# build libequihashverifynew
(cd ../Native/libequihashverifynew && make)
cp ../Native/libequihashverifynew/libequihashverifynew.so $BUILDIR
(cd ../Native/libequihashverifynew && make clean)
