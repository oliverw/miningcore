#!/bin/bash
set -x
set -e

#Param(s)
SRC_DIR=$1
OUT_DIR=$2
BUILD_CONFIG=$3

#Dependencies
sudo apt-get update -y && sudo apt-get install -y --no-install-recommends cmake build-essential libssl-dev libsodium-dev pkg-config libboost-all-dev libzmq5

#Libs
cd $SRC_DIR/src/Native/libmultihash && make clean && make && yes| cp -rf libmultihash.so $OUT_DIR
cd $SRC_DIR/src/Native/libcryptonight && make clean && make && yes| cp -rf libcryptonight.so $OUT_DIR
cd $SRC_DIR/src/Native/libcryptonote && make clean && make && yes| cp -rf libcryptonote.so $OUT_DIR

#Build
cd $SRC_DIR/src && dotnet build --configuration $BUILD_CONFIG --output $OUT_DIR
