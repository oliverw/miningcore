
[![Build status](https://ci.appveyor.com/api/projects/status/nbvaa55gu3icd1q8?svg=true)](https://ci.appveyor.com/project/oliverw/miningcore)
[![Docker Build Statu](https://img.shields.io/docker/build/coinfoundry/miningcore-docker.svg)](https://hub.docker.com/r/coinfoundry/miningcore-docker/)
[![Docker Stars](https://img.shields.io/docker/stars/coinfoundry/miningcore-docker.svg)](https://hub.docker.com/r/coinfoundry/miningcore-docker/)
[![Docker Pulls](https://img.shields.io/docker/pulls/coinfoundry/miningcore-docker.svg)]()
[![license](https://img.shields.io/github/license/mashape/apistatus.svg)]()

## Miningcore

Miningcore a the multi-currency stratum-engine.

Even though the pool engine can be used to run a production-pool, doing so currently requires to
develop your own website frontend talking to the pool's API-Endpoint at http://127.0.0.1:4000.
This is going to change in the future.

### Features

- Supports clusters of pools each running individual currencies
- Ultra-low-latency Stratum implementation using asynchronous I/O (LibUv)
- Adaptive share difficulty ("vardiff")
- PoW validation (hashing) using native code for maximum performance
- Session management for purging DDoS/flood initiated zombie workers
- Payment processing
- Banning System for banning peers that are flooding with invalid shares
- Live Stats API on Port 4000
- POW (proof-of-work) & POS (proof-of-stake) support
- Detailed per-pool logging to console & filesystem
- Runs on Linux and Windows

### Coins

Coin | Implemented | Tested | Planned | Notes
:--- | :---: | :---: | :---: | :---:
Bitcoin | Yes | Yes | |
Bitcoin Cash | Yes | Yes | |
Bitcoin Gold | Yes | Yes | |
Bitcoin Diamond | Yes | Yes | |
Bitcoin Private | Yes | Yes | |
Litecoin | Yes | Yes | |
Zcash | Yes | Yes | |
Monero | Yes | Yes | |
Ethereum | Yes | Yes | | Requires [Parity](https://github.com/paritytech/parity/releases)
Ethereum Classic | Yes | Yes | | Requires [Parity](https://github.com/paritytech/parity/releases)
DASH | Yes | Yes | |
Vertcoin | Yes | Yes | |
Monacoin | Yes | Yes | |
Groestlcoin | Yes | Yes | |
Dogecoin | Yes | No | |
DigiByte | Yes | Yes | |
Namecoin | Yes | No | |
Viacoin | Yes | No | |
Peercoin | Yes | No | |
Ravencoin | Yes | Yes | |
MoonCoin | Yes | Yes | |

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

- [.Net Core 2.1 Runtime](https://www.microsoft.com/net/download/core)
- [PostgreSQL Database](https://www.postgresql.org/)
- Coin Daemon (per pool)

### Runtime Requirements on Linux

- [.Net Core 2.1 SDK](https://www.microsoft.com/net/download/core)
- [PostgreSQL Database](https://www.postgresql.org/)
- On Linux you also need to install the libzmq package for your platform (Ubuntu/Debian: libzmq5, CentOS epel: zeromq)
- Coin Daemon (per pool)
- MiningCore needs to be built from source on Linux. Refer to the section further down below for instructions.

### Running pre-built Release Binaries on Windows

- Download miningcore-win-x64.zip from the latest [Release](https://github.com/coinfoundry/miningcore/releases)
- Extract the Archive
- Setup the database as outlined below
- Create a configuration file <code>config.json</code> as described [here](https://github.com/coinfoundry/miningcore/wiki/Configuration)
- Run <code>dotnet MiningCore.dll -c config.json</code>

### PostgreSQL Database setup

Create the database:

```console
$ createuser miningcore
$ createdb miningcore
$ psql (enter the password for postgres)
```

Run the query after login:

```sql
alter user miningcore with encrypted password 'some-secure-password';
grant all privileges on database miningcore to miningcore;
```

Import the database schema:

```console
$ wget https://raw.githubusercontent.com/coinfoundry/miningcore/master/src/MiningCore/Persistence/Postgres/Scripts/createdb.sql
$ psql -d miningcore -U miningcore -f createdb.sql
```

### [Configuration](https://github.com/coinfoundry/miningcore/wiki/Configuration)

### [API](https://github.com/coinfoundry/miningcore/wiki/API)

### Building from Source

#### Building on Ubuntu 16.04

```console
$ wget -q https://packages.microsoft.com/config/ubuntu/16.04/packages-microsoft-prod.deb
$ sudo dpkg -i packages-microsoft-prod.deb
$ sudo apt-get update -y
$ sudo apt-get install apt-transport-https -y
$ sudo apt-get update -y
$ sudo apt-get -y install dotnet-sdk-2.1 git cmake build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev libzmq5
$ git clone https://github.com/coinfoundry/miningcore
$ cd miningcore/src/MiningCore
$ ./linux-build.sh
```

#### Building on Windows

Download and install the [.Net Core 2.1 SDK](https://www.microsoft.com/net/download/core)

```dosbatch
> git clone https://github.com/coinfoundry/miningcore
> cd miningcore/src/MiningCore
> windows-build.bat
```

#### Building on Windows - Visual Studio

- Download and install the [.Net Core 2.1 SDK](https://www.microsoft.com/net/download/core)
- Install [Visual Studio 2017](https://www.visualstudio.com/vs/). Visual Studio Community Edition is fine.
- Open `MiningCore.sln` in VS 2017


#### After successful build

Create a configuration file <code>config.json</code> as described [here](https://github.com/coinfoundry/miningcore/wiki/Configuration)

```
cd ../../build
dotnet MiningCore.dll -c config.json
```
