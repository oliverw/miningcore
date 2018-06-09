#!/bin/bash

BUILDIR=${1:-/dotnetapp}

echo "Building into $BUILDIR"

# build miningcore
dotnet build -c Release -o $BUILDIR -r alpine.3.7-x64 && \
dotnet publish -c Release -o $BUILDIR -r alpine.3.7-x64 && \
dotnet publish -c Release -o /dotnetapp_linux -r linux-x64

# build libcryptonote
(cd ../Native/libcryptonote && make)
cp ../Native/libcryptonote/libcryptonote.so $BUILDIR
(cd ../Native/libcryptonote && make clean)

# build libmultihash
(cd ../Native/libmultihash && make)
cp ../Native/libmultihash/libmultihash.so $BUILDIR
(cd ../Native/libmultihash && make clean)

