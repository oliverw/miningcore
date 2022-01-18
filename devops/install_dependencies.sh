#!/bin/bash

sudo apt-get update -y

which dotnet
if [ $? -ne 0 ];
then
   wget https://packages.microsoft.com/config/ubuntu/20.10/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
   sudo dpkg -i packages-microsoft-prod.deb
   sudo apt-get update; \
   sudo apt-get install -y apt-transport-https && \
   sudo apt-get update && \
   sudo apt-get install -y dotnet-sdk-5.0

    echo "installed dotnet 5"
else
    echo "psql already installed."
fi


which psql
if [ $? -ne 0 ];
then
    sudo sh -c 'echo "deb http://apt.postgresql.org/pub/repos/apt $(lsb_release -cs)-pgdg main" > /etc/apt/sources.list.d/pgdg.list'
    wget --quiet -O - https://www.postgresql.org/media/keys/ACCC4CF8.asc | sudo apt-key add -


    sudo apt-get -y install postgresql-client-12
    echo "installed psql"
else
    echo "psql already installed."
fi

sudo apt-get -y install git cmake build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev libzmq5


