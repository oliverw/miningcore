#!/bin/bash

dbhost=$1
dbuser=$2
dbpass=$3
poolpass=$4

if [ $# -ne 4 ]
  then
    echo "invalid command line args"
    exit 1
fi

which psql
if [ $? -ne 0 ];
  then
    echo "psql not installed.  please install dependencies"
    exit 1
  else
    echo "psql already installed."
fi

echo "Creating miningcore user"
psql "host=$dbhost.postgres.database.azure.com port=5432 dbname=postgres user=$dbuser@$dbhost password=$dbpass sslmode=require" -f initial_postgres.sql -v pw="'$poolpass'" -v su="'$dbuser'"


echo "Creating initial schema"
psql "host=$dbhost.postgres.database.azure.com port=5432 dbname=miningcore user=miningcore@$dbhost password=$poolpass sslmode=require" -f createdb.sql

echo "Registering eth1 pool"
psql "host=$dbhost.postgres.database.azure.com port=5432 dbname=miningcore user=miningcore@$dbhost password=$poolpass sslmode=require" -f addeth1.sql

