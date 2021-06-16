[![Build status](https://ci.appveyor.com/api/projects/status/nbvaa55gu3icd1q8?svg=true)](https://ci.appveyor.com/project/oliverw/miningcore)
[![license](https://img.shields.io/github/license/mashape/apistatus.svg)]()

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

Refer to [this file](https://github.com/coinfoundry/miningcore/blob/master/src/Miningcore/coins.json) for a complete list.

#### Monero

- Miningcore utilizes RandomX's light-mode by default which consumes only 256 MB of memory. A mondern (2021) era CPU will be able to handle ~ 50 shares per second in this mode.
- If you are running into throughput problems on your pool you can either increase the number of RandomX virtual machines in light-mode by adding `"randomXVmCount": x` to your pool configuration or activate fast-mode by adding `"randomXFlagsAdd": "RANDOMX_FLAG_FULL_MEM"` to the pool configuration. Fast mode increases performance by 10x but requires roughly 3 GB of RAM per RandomX-VM.

#### ZCash

- Pools needs to be configured with both a t-addr and z-addr (new configuration property "z-address" of the pool configuration element)
- First configured zcashd daemon needs to control both the t-addr and the z-addr (have the private key)
- To increase the share processing throughput it is advisable to increase the maximum number of concurrent equihash solvers through the new configuration property "equihashMaxThreads" of the cluster configuration element. Increasing this value by one increases the peak memory consumption of the pool cluster by 1 GB.
- Miners may use both t-addresses and z-addresses when connecting to the pool

### Donations

This software comes with a built-in donation of 0.1% per block-reward to support the ongoing development of this project. You can also send donations directly to the following accounts:

* BTC:  `17QnVor1B6oK1rWnVVBrdX9gFzVkZZbhDm`
* LTC:  `LTK6CWastkmBzGxgQhTTtCUjkjDA14kxzC`
* DOGE: `DGDuKRhBewGP1kbUz4hszNd2p6dDzWYy9Q`
* ETH:  `0xcb55abBfe361B12323eb952110cE33d5F28BeeE1`
* ETC:  `0xF8cCE9CE143C68d3d4A7e6bf47006f21Cfcf93c0`
* DASH: `XqpBAV9QCaoLnz42uF5frSSfrJTrqHoxjp`
* ZEC:  `t1YHZHz2DGVMJiggD2P4fBQ2TAPgtLSUwZ7`
* BTG:  `GQb77ZuMCyJGZFyxpzqNfm7GB1rQreP4n6`
* XMR: `475YVJbPHPedudkhrcNp1wDcLMTGYusGPF5fqE7XjnragVLPdqbCHBdZg3dF4dN9hXMjjvGbykS6a77dTAQvGrpiQqHp2eH`

### Runtime Requirements on Windows

- [.NET 5 Runtime](https://dotnet.microsoft.com/download/dotnet/5.0)
- [PostgreSQL Database](https://www.postgresql.org/)
- Coin Daemon (per pool)

### Runtime Requirements on Linux

- [.NET 5 SDK](https://dotnet.microsoft.com/download/dotnet/5.0)
- [PostgreSQL Database](https://www.postgresql.org/)
- Coin Daemon (per pool)
- Miningcore needs to be built from source on Linux. Refer to the section further down below for instructions.

### Running pre-built Release Binaries on Windows

- Download miningcore-win-x64.zip from the latest [Release](https://github.com/coinfoundry/miningcore/releases)
- Extract the Archive
- Setup the database as outlined below
- Create a configuration file <code>config.json</code> as described [here](https://github.com/coinfoundry/miningcore/wiki/Configuration)
- Run <code>dotnet Miningcore.dll -c config.json</code>

### Basic PostgreSQL Database setup

Create the database:

```console
$ createuser miningcore
$ createdb miningcore
$ psql (enter the password for postgres)
```

Inside psql execute:

```sql
alter user miningcore with encrypted password 'some-secure-password';
grant all privileges on database miningcore to miningcore;
```

Import the database schema:

```console
$ wget https://raw.githubusercontent.com/coinfoundry/miningcore/master/src/Miningcore/Persistence/Postgres/Scripts/createdb.sql
$ psql -d miningcore -U miningcore -f createdb.sql
```

### Advanced PostgreSQL Database setup

If you are planning to run a Multipool-Cluster, the simple setup might not perform well enough under high load. In this case you are strongly advised to use PostgreSQL 11 or higher. After performing the steps outlined in the basic setup above, perform these additional steps:

**WARNING**: The following step will delete all recorded shares. Do **NOT** do this on a production pool unless you backup your <code>shares</code> table using <code>pg_backup</code> first!

```console
$ wget https://raw.githubusercontent.com/coinfoundry/miningcore/master/src/Miningcore/Persistence/Postgres/Scripts/createdb_postgresql_11_appendix.sql
$ psql -d miningcore -U miningcore -f createdb_postgresql_11_appendix.sql
```

After executing the command, your <code>shares</code> table is now a [list-partitioned table](https://www.postgresql.org/docs/11/ddl-partitioning.html) which dramatically improves query performance, since almost all database operations Miningcore performs are scoped to a certain pool.

The following step needs to performed **once for every new pool** you add to your cluster. Be sure to **replace all occurences** of <code>mypool1</code> in the statement below with the id of your pool from your Miningcore configuration file:

```sql
CREATE TABLE shares_mypool1 PARTITION OF shares FOR VALUES IN ('mypool1');
```

Once you have done this for all of your existing pools you should now restore your shares from backup.

### [Configuration](https://github.com/coinfoundry/miningcore/wiki/Configuration)

### [API](https://github.com/coinfoundry/miningcore/wiki/API)

### Building from Source

#### Building on Ubuntu 20.04

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

#### Building on Windows

Download and install the [.NET 5 SDK](https://dotnet.microsoft.com/download/dotnet/5.0)

```dosbatch
> git clone https://github.com/coinfoundry/miningcore
> cd miningcore/src/Miningcore
> dotnet publish -c Release --framework net5.0  -o ..\..\build
```

#### Building on Windows - Visual Studio

- Install [Visual Studio 2019](https://www.visualstudio.com/vs/). Visual Studio Community Edition is fine.
- Open `Miningcore.sln` in Visual Studio


#### After successful build

Create a configuration file <code>config.json</code> as described [here](https://github.com/coinfoundry/miningcore/wiki/Configuration)

```console
$ cd ../../build
$ Miningcore -c config.json
```

## Running a production pool

A public production pool requires a web-frontend for your users to check their hashrate, earnings etc. Miningcore does not include such frontend but there are several community projects that can be used as starting point.
