[![Build status](https://ci.appveyor.com/api/projects/status/nbvaa55gu3icd1q8?svg=true)](https://ci.appveyor.com/project/oliverw/miningcore)
[![license](https://img.shields.io/github/license/mashape/apistatus.svg)]()

<img src="https://github.com/coinfoundry/miningcore/raw/master/logo.png" width="150">

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

## Support

Commercial support directly by the maintainer is available through [contact@coinfoundry.org](mailto:contact@coinfoundry.org). There are various support packages, ranging from basic installation support to implementation of new crypto-currencies.

For general questions visit the [Discussions Area](https://github.com/coinfoundry/miningcore/discussions).

## Running Miningcore

### Linux: pre-built binaries

- Install [.NET 5 Runtime](https://dotnet.microsoft.com/download/dotnet/5.0)
- For Debian/Ubuntu, install these packages
  - `postgresql-11` (or higher, the higher the better)
  - `libzmq5`
  - `libboost-system1.67.0`
  - `libboost-date-time1.67.0`
- Download `miningcore-linux-ubuntu-x64.tar.gz` from the latest [Release](https://github.com/coinfoundry/miningcore/releases)
- Extract the archive
- Setup the database as outlined below
- Create a configuration file `config.json` as described [here](https://github.com/coinfoundry/miningcore/wiki/Configuration)
- Run `dotnet Miningcore.dll -c config.json`

### Windows: pre-built binaries

- Install [.NET 5 Runtime](https://dotnet.microsoft.com/download/dotnet/5.0)
- Install [PostgreSQL Database](https://www.postgresql.org/)
- Download `miningcore-win-x64.zip` from the latest [Release](https://github.com/coinfoundry/miningcore/releases)
- Extract the Archive
- Setup the database as outlined below
- Create a configuration file `config.json` as described [here](https://github.com/coinfoundry/miningcore/wiki/Configuration)
- Run `dotnet Miningcore.dll -c config.json`

## Database setup

Miningcore currently requires PostgreSQL 10 or higher.

Create the database:

```console
$ createuser miningcore
$ createdb miningcore
$ psql (enter the password for postgres)
```

Inside `psql` execute:

```sql
alter user miningcore with encrypted password 'some-secure-password';
grant all privileges on database miningcore to miningcore;
```

Import the database schema:

```console
$ wget https://raw.githubusercontent.com/coinfoundry/miningcore/master/src/Miningcore/Persistence/Postgres/Scripts/createdb.sql
$ psql -d miningcore -U miningcore -f createdb.sql
```

### Advanced setup

If you are planning to run a Multipool-Cluster, the simple setup might not perform well enough under high load. In this case you are strongly advised to use PostgreSQL 11 or higher. After performing the steps outlined in the basic setup above, perform these additional steps:

**WARNING**: The following step will delete all recorded shares. Do **NOT** do this on a production pool unless you backup your `shares` table using `pg_backup` first!

```console
$ wget https://raw.githubusercontent.com/coinfoundry/miningcore/master/src/Miningcore/Persistence/Postgres/Scripts/createdb_postgresql_11_appendix.sql
$ psql -d miningcore -U miningcore -f createdb_postgresql_11_appendix.sql
```

After executing the command, your `shares` table is now a [list-partitioned table](https://www.postgresql.org/docs/11/ddl-partitioning.html) which dramatically improves query performance, since almost all database operations Miningcore performs are scoped to a certain pool.

The following step needs to performed **once for every new pool** you add to your cluster. Be sure to **replace all occurences** of `mypool1` in the statement below with the id of your pool from your Miningcore configuration file:

```sql
CREATE TABLE shares_mypool1 PARTITION OF shares FOR VALUES IN ('mypool1');
```

Once you have done this for all of your existing pools you should now restore your shares from backup.

## Configuration

Please refer to this Wiki Page: https://github.com/coinfoundry/miningcore/wiki/Configuration

## Building from Source

### Building on Ubuntu 20.04

```console
$ wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
$ sudo dpkg -i packages-microsoft-prod.deb
$ sudo apt-get update
$ sudo apt-get install -y apt-transport-https
$ sudo apt-get update
$ sudo apt-get -y install dotnet-sdk-5.0 git cmake build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev libzmq5
$ git clone https://github.com/coinfoundry/miningcore
$ cd miningcore/src/Miningcore
$ dotnet publish -c Release --framework net5.0  -o ../../build
```

### Building on Windows

Download and install the [.NET 5 SDK](https://dotnet.microsoft.com/download/dotnet/5.0)

```dosbatch
> git clone https://github.com/coinfoundry/miningcore
> cd miningcore/src/Miningcore
> dotnet publish -c Release --framework net5.0  -o ..\..\build
```

### Building on Windows - Visual Studio

- Install [Visual Studio 2019](https://www.visualstudio.com/vs/). Visual Studio Community Edition is fine.
- Open `Miningcore.sln` in Visual Studio


### After successful build

Create a configuration file `config.json` as described [here](https://github.com/coinfoundry/miningcore/wiki/Configuration)

```console
$ cd ../../build
$ Miningcore -c config.json
```

### Supported Currencies

Refer to [this file](https://github.com/coinfoundry/miningcore/blob/master/src/Miningcore/coins.json) for a complete list.

### Caveats

#### Monero

- Monero's Wallet Daemon (monero-wallet-rpc) relies on HTTP digest authentication for authentication which is currently not supported by Miningcore. Therefore monero-wallet-rpc must be run with the `--disable-rpc-login` option. It is advisable to mitigate the resulting security risk by putting monero-wallet-rpc behind a reverse proxy like nginx with basic-authentication.
- Miningcore utilizes RandomX's light-mode by default which consumes only **256 MB of memory per RandomX-VM**. A modern (2021) era CPU will be able to handle ~ 50 shares per second in this mode.
- If you are running into throughput problems on your pool you can either increase the number of RandomX virtual machines in light-mode by adding `"randomXVmCount": x` to your pool configuration where x is at maximum equal to the machine's number of processor cores. Alternatively you can activate fast-mode by adding `"randomXFlagsAdd": "RANDOMX_FLAG_FULL_MEM"` to the pool configuration. Fast mode increases performance by 10x but requires roughly **3 GB of RAM per RandomX-VM**.

#### ZCash

- Pools needs to be configured with both a t-addr and z-addr (new configuration property "z-address" of the pool configuration element)
- First configured zcashd daemon needs to control both the t-addr and the z-addr (have the private key)
- To increase the share processing throughput it is advisable to increase the maximum number of concurrent equihash solvers through the new configuration property "equihashMaxThreads" of the cluster configuration element. Increasing this value by one increases the peak memory consumption of the pool cluster by 1 GB.
- Miners may use both t-addresses and z-addresses when connecting to the pool

#### Ethereum

- Miningcore implements the [Ethereum stratum mining protocol](https://github.com/nicehash/Specifications/blob/master/EthereumStratum_NiceHash_v1.0.0.txt) authored by NiceHash. This protocol is implemented by all major Ethereum miners.
- Claymore Miner must be configured to communicate using this protocol by supplying the `-esm 3` command line option
- Genoil's `ethminer` must be configured to communicate using this protocol by supplying the `-SP 2` command line option

#### Vertcoin

- Be sure to copy the file `verthash.dat` from your vertcoin blockchain folder to your Miningcore server
- In your Miningcore config file add this property to your vertcoin pool configuration: `"vertHashDataFile": "/path/to/verthash.dat",`

## API

Miningcore comes with an integrated REST API. Please refer to this page for instructions: https://github.com/coinfoundry/miningcore/wiki/API

## Running a production pool

A public production pool requires a web-frontend for your users to check their hashrate, earnings etc. Miningcore does not include such frontend but there are several community projects that can be used as starting point.

## Donations

This software comes with a built-in donation of 0.1% per block-reward to support the ongoing development of this project. You can also send donations directly to the following accounts:

* XMR: `46S2AEwYmD9fnmZkxCpXf1T3U3DyEq3Ekb8Lg9kgUMGABn9Fp9q5nE2fBcXebrjrXfZHy5uC5HfLE6X4WLtSm35wUr9Mh46`
* BTC:  `bc1quzdczlpfn3n4xvpdz0x9h79569afhg0ashwxxp`
* BCH:  `qrf6uhhapq7fgkjv2ce2hcjqpk8ec2zc25et4xsphv`
* LTC:  `LTK6CWastkmBzGxgQhTTtCUjkjDA14kxzC`
* DOGE: `DGDuKRhBewGP1kbUz4hszNd2p6dDzWYy9Q`
* ETH:  `0xcb55abBfe361B12323eb952110cE33d5F28BeeE1`
* ETC:  `0xF8cCE9CE143C68d3d4A7e6bf47006f21Cfcf93c0`
* DASH: `XqpBAV9QCaoLnz42uF5frSSfrJTrqHoxjp`
* ZEC:  `t1YHZHz2DGVMJiggD2P4fBQ2TAPgtLSUwZ7`
* BTG:  `GQb77ZuMCyJGZFyxpzqNfm7GB1rQreP4n6`
