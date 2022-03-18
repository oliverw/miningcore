#!/bin/bash
set -x
set -e

#Param(s)
SRC_DIR=$1
OUT_DIR=$2
BUILD_CONFIG=$3

#Dependencies
sudo apt-get update -y && sudo apt-get install -y --no-install-recommends default-jdk jq cmake build-essential libssl-dev libsodium-dev pkg-config libboost-all-dev libzmq5

#Libs
#cd $SRC_DIR/src/Native/libmultihash && make clean && make && yes| cp -rf libmultihash.so $OUT_DIR
#cd $SRC_DIR/src/Native/libcryptonight && make clean && make && yes| cp -rf libcryptonight.so $OUT_DIR
#cd $SRC_DIR/src/Native/libcryptonote && make clean && make && yes| cp -rf libcryptonote.so $OUT_DIR

#Build
cd $SRC_DIR/src && dotnet build --configuration $BUILD_CONFIG --output $OUT_DIR

#Start local parity
cd $SRC_DIR/tools/parity-poa-linux
chmod +x * && nohup ./parity.sh >/dev/null 2>&1 &

#Start mock api
cd $SRC_DIR/tools/mock-server/
chmod +x * && ./etherscan-mock.sh

#Update config
cd $SRC_DIR
jq '.persistence.postgres.host="'$PERSISTENCE_POSTGRES_HOST'"|.persistence.postgres.user="'$PERSISTENCE_POSTGRES_USER'"|.persistence.postgres.password="'$PERSISTENCE_POSTGRES_PASSWORD'"|.persistence.cosmos.endpointUrl="'$PERSISTENCE_COSMOS_ENDPOINTURL'"|.persistence.cosmos.authorizationKey="'$PERSISTENCE_COSMOS_AUTHORIZATIONKEY'"|.persistence.cosmos.databaseId="'$PERSISTENCE_COSMOS_DATABASEID'"' $OUT_DIR/config_test.json > tmp.$$.json && mv tmp.$$.json $OUT_DIR/config_test.json
