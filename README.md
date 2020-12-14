[![Build status](https://ci.appveyor.com/api/projects/status/nbvaa55gu3icd1q8?svg=true)](https://ci.appveyor.com/project/oliverw/miningcore)
[![Docker Build Statu](https://img.shields.io/docker/build/coinfoundry/miningcore-docker.svg)](https://hub.docker.com/r/coinfoundry/miningcore-docker/)
[![Docker Stars](https://img.shields.io/docker/stars/coinfoundry/miningcore-docker.svg)](https://hub.docker.com/r/coinfoundry/miningcore-docker/)
[![Docker Pulls](https://img.shields.io/docker/pulls/coinfoundry/miningcore-docker.svg)]()
[![license](https://img.shields.io/github/license/mashape/apistatus.svg)]()

MinerNL - Miningcore Stratum Pool
=================================

### Changes in this Miningcore fork
- Added nice Web frontend [https://miningcore.com](https://miningcore.com)
- Pool time set to UTC time zone
  Local time convertion should be used in the web frondend as that can be anyone on the globe
- Faster mining statistics calculation
- Added stats setting in config.json
```config
	"statistics": {
		// Stats broadcast (seconds)
		"statsUpdateInterval": 60,
		// Stats calculation window (minutes)
		"hashrateCalculationWindow": 5,
		// Stats DB cleanup interval (hours)
		"statsCleanupInterval": 48,
		// Stats history to cleanup is DB. older then x (days)
		"statsDBCleanupHistory": 365
	},
```

### Features

- Supports clusters of pools each running individual currencies
- Ultra-low-latency, multi-threaded Stratum implementation using asynchronous I/O
- Adaptive share difficulty ("vardiff")
- PoW validation (hashing) using native code for maximum performance
- Session management for purging DDoS/flood initiated zombie workers
- Payment processing
- Banning System
- Live Stats [API](https://github.com/coinfoundry/miningcore/wiki/API) on Port 4000
- WebSocket streaming of notable events like Blocks found, Blocks unlocked, Payments and more
- POW (proof-of-work) & POS (proof-of-stake) support
- Detailed per-pool logging to console & filesystem
- Runs on Linux and Windows
- [Gitter Channel](https://gitter.im/miningcore/Lobby)

### Supported Coins

Refer to [this file](https://github.com/minernl/miningcore/blob/master/src/Miningcore/coins.json) for a complete list.

#### Ethereum

Miningcore implements the [Ethereum stratum mining protocol](https://github.com/nicehash/Specifications/blob/master/EthereumStratum_NiceHash_v1.0.0.txt) authored by NiceHash. This protocol is implemented by all major Ethereum miners.

- Claymore Miner must be configured to communicate using this protocol by supplying the <code>-esm 3</code> command line option
- Genoil's ethminer must be configured to communicate using this protocol by supplying the <code>-SP 2</code> command line option

#### ZCash

- Pools needs to be configured with both a t-addr and z-addr (new configuration property "z-address" of the pool configuration element)
- First configured zcashd daemon needs to control both the t-addr and the z-addr (have the private key)
- To increase the share processing throughput it is advisable to increase the maximum number of concurrent equihash solvers through the new configuration property "equihashMaxThreads" of the cluster configuration element. Increasing this value by one increases the peak memory consumption of the pool cluster by 1 GB.
- Miners may use both t-addresses and z-addresses when connecting to the pool

### Donations

This software comes with a built-in donation of 0.1% per block-reward to support the ongoing development of this project. 
You can also send donations directly to the developemers using the following accounts:

* BTC:  `3QT2WreQtanPHcMneg9LT2aH3s5nrSZsxr`
* LTC:  `LTVnLEv8Xj6emGbf981nTyN54Mnyjbfgrg`
* DASH: `Xc2vm9SfRn8t1hyQgqi8Zrt3oFeGcQtw`
* ETH:  `0xBfD360CDd9014Bc5B348B65cBf79F78381694f4E`
* ETC:  `0xF4BFFC324bbeB63348F137B84f8d1Ade17B507E4`
* XMR: `44riGcQcDp4EsboDJP284CFCnJ2qP7y8DAqGC4D9WtVbEqzxQ3qYXAUST57u5FkrVF7CXhsEc63QNWazJ5b9ygwBJBtB2kT`
* ZEC:  `t1JtJtxTdgXCaYm1wzRfMRkGTJM4qLcm4FQ`

### Running Miningcore on Windows

- [.Net Core 3.1 Runtime](https://www.microsoft.com/net/download/core)
- [PostgreSQL Database v12 or higher](https://www.postgresql.org/)
- Coin Daemon (per pool)

### Running pre-built Release Binaries on Windows

- Download miningcore-win-x64.zip from the latest [Release](https://github.com/minernl/miningcore/releases)
- Extract the Archive
- Setup the database as outlined below
- Create a configuration file <code>config.json</code> as described [here](https://github.com/minernl/miningcore/wiki/Configuration)
- Run <code>dotnet Miningcore.dll -c config.json</code>


### Running Miningcore on Linux

- [.Net Core 3.1 SDK](https://www.microsoft.com/net/download/core)
- [PostgreSQL Database v12 or higher](https://www.postgresql.org/)
- Create the database config:
```console
$ sudo -i -u postgres							(<- you should see your logged in as postgres user)
$ psql
postgres=# CREATE USER miningcore WITH ENCRYPTED PASSWORD 'some-secure-password';
postgres=# CREATE DATABASE miningcore;
postgres=# ALTER DATABASE miningcore OWNER TO miningcore;
postgres=# ALTER USER postgres WITH PASSWORD 'new_password';
postgres=# grant all privileges on database miningcore to miningcore;
postgres=# \list							(<- show databases and privileges)

                               List of databases
    Name    |  Owner   | Encoding | Collate |  Ctype  |    Access privileges
------------+----------+----------+---------+---------+-------------------------
 miningcore | postgres | UTF8     | C.UTF-8 | C.UTF-8 | =Tc/postgres           +
            |          |          |         |         | postgres=CTc/postgres  +
            |          |          |         |         | miningcore=CTc/postgres
 postgres   | postgres | UTF8     | C.UTF-8 | C.UTF-8 |
 template0  | postgres | UTF8     | C.UTF-8 | C.UTF-8 | =c/postgres            +
            |          |          |         |         | postgres=CTc/postgres
 template1  | postgres | UTF8     | C.UTF-8 | C.UTF-8 | =c/postgres            +
            |          |          |         |         | postgres=CTc/postgres
(4 rows)

postgres-# \quit
$ exit									(<- exit user postgres)

```
- Import Miningcore database tables
```console
$ sudo wget https://raw.githubusercontent.com/minernl/miningcore/master/src/Miningcore/Persistence/Postgres/Scripts/createdb.sql

$ sudo -u postgres -i
$ psql -d miningcore -f createdb.sql
```
- Coin Daemon (per pool)
- Miningcore needs to be built from source on Linux.
  (example below is used on Ubuntu 20.04. Change the microsoft.com package line to you own OS)
```console
$ sudo wget -q https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb
$ sudo dpkg -i packages-microsoft-prod.deb
$ sudo apt-get update -y
$ sudo apt-get install apt-transport-https -y
$ sudo apt-get update -y
$ sudo apt-get install git cmake build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev libzmq5
$ sudo git clone https://github.com/minernl/miningcore
$ cd miningcore/src/Miningcore
$ dotnet publish -c Release --framework netcoreapp3.1  -o ../../build
```
- Running Miningcore (after build)
Create a configuration file config.json as described here
```console
cd ../../build
dotnet Miningcore.dll -c config.json
```



### Advanced PostgreSQL Database setup

The following step needs to performed **once for every new pool** you add to your cluster. Be sure to **replace all occurences** of <code>mypool1</code> in the statement below with the id of your pool from your Miningcore configuration file:

```sql
CREATE TABLE shares_mypool1 PARTITION OF shares FOR VALUES IN ('mypool1');
```


### [Configuration](https://github.com/minernl/miningcore/wiki/Configuration)

### [API](https://github.com/minernl/miningcore/wiki/API)


#### Building on Windows

Download and install the [.Net Core 3.1 SDK](https://www.microsoft.com/net/download/core)

```dosbatch
> git clone https://github.com/minernl/miningcore
> cd miningcore/src/Miningcore
> dotnet publish -c Release --framework netcoreapp3.1  -o ..\..\build
```

#### Building on Windows - VISUAL STUDIO

- Download and install the [.Net Core 3.1 SDK](https://www.microsoft.com/net/download/core)
- Install [Visual Studio 2019](https://www.visualstudio.com/vs/). Visual Studio Community Edition is fine.
- Open `Miningcore.sln` in VS 2019


#### Running Miningcore (after build)

Create a configuration file <code>config.json</code> as described [here](https://github.com/minernl/miningcore/wiki/Configuration)

```
cd ../../build
dotnet Miningcore.dll -c config.json
```

## Running a production pool

A public production pool requires a web-frontend for your users to check their hashrate, earnings etc. 
You can use the web frontend that come with this fork [Miningcore.Web](https://github.com/minernl/miningcore/src/Miningcore.WebUI)

Feel free to discuss ideas/issues with fellow pool operators using our [Gitter Channel](https://gitter.im/miningcore/Lobby).
