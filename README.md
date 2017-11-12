[![Build status](https://ci.appveyor.com/api/projects/status/nbvaa55gu3icd1q8?svg=true)](https://ci.appveyor.com/project/oliverw/miningcore)
[![Docker Build Statu](https://img.shields.io/docker/build/coinfoundry/miningcore-docker.svg)](https://hub.docker.com/r/coinfoundry/miningcore-docker/)
[![Docker Stars](https://img.shields.io/docker/stars/coinfoundry/miningcore-docker.svg)](https://hub.docker.com/r/coinfoundry/miningcore-docker/)
[![Docker Pulls](https://img.shields.io/docker/pulls/coinfoundry/miningcore-docker.svg)]()
[![license](https://img.shields.io/github/license/mashape/apistatus.svg)]()

## MiningCore

MiningCore is the multi-currency stratum-engine powering [poolmining.org](https://poolmining.org)

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
Litecoin | Yes | Yes | |
Zcash | Yes | Yes | |
Monero | Yes | Yes | |
Ethereum | Yes | Yes | | Requires [Parity](https://github.com/paritytech/parity/releases)
Ethereum Classic | Yes | Yes | | Requires [Parity](https://github.com/paritytech/parity/releases)
Expanse | Yes | Yes | | Requires [Parity](https://github.com/paritytech/parity/releases)
DASH | Yes | Yes | |
Bitcoin Cash | Yes | Yes | |
Vertcoin | Yes | Yes | |
Monacoin | Yes | Yes | |
Groestlcoin | Yes | Yes | |
Dogecoin | Yes | No | |
DigiByte | Yes | No | |
Namecoin | Yes | No | |
Viacoin | Yes | No | |
Peercoin | Yes | No | |

#### Ethereum

MiningCore implements the [Ethereum stratum mining protocol](https://github.com/nicehash/Specifications/blob/master/EthereumStratum_NiceHash_v1.0.0.txt) authored by NiceHash. This protocol is implemented by all major Ethereum miners.

- Claymore Miner must be configured to communicate using this protocol by supplying the <code>-esm 3</code> command line option
- Genoil's ethminer must be configured to communicate using this protocol by supplying the <code>-SP 2</code> command line option

#### ZCash

- Pools needs to be configured with both a t-addr and z-addr (new configuration property "z-address" of the pool configuration element)
- First configured zcashd daemon needs to control both the t-addr and the z-addr (have the private key)
- To increase the share processing throughput it is advisable to increase the maximum number of concurrent equihash solvers through the new configuration property "equihashMaxThreads" of the cluster configuration element. Increasing this value by one increases the peak memory consumption of the pool cluster by 1 GB.

### Runtime Requirements

- [.Net Core 2.0 Runtime](https://www.microsoft.com/net/download/core#/runtime)
- [PostgreSQL Database](https://www.postgresql.org/)
- Coin Daemon (per pool)

### PostgreSQL Database setup

Create the database:

```bash
createuser miningcore
createdb miningcore
psql (enter the password for postgressql)
```
```sql
alter user miningcore with encrypted password 'some-secure-password';
grant all privileges on database miningcore to miningcore;
```

Import the database schema:

```bash
wget https://raw.githubusercontent.com/coinfoundry/miningcore/master/src/MiningCore/Persistence/Postgres/Scripts/createdb.sql
psql -d miningcore -U miningcore -f createdb.sql
```

### [Configuration](https://github.com/coinfoundry/miningcore/wiki/Configuration)

### [API](https://github.com/coinfoundry/miningcore/wiki/API)

### Docker

The official [MiningCore docker-image](https://hub.docker.com/r/coinfoundry/miningcore-docker/) expects a valid pool configuration file as volume argument:

```bash
$ docker run -d -p 3032:3032 -v /path/to/config.json:/config.json:ro coinfoundry/miningcore-docker
```

You also need to expose all stratum ports specified in your configuration file.

### Building from Source (Shell)

Install the [.Net Core 2.0 SDK](https://www.microsoft.com/net/download/core) for your platform

```bash
git clone https://github.com/coinfoundry/miningcore
cd miningcore/src/MiningCore
```

#### Linux

Install dev-dependencies (Ubuntu)

```bash
apt-get update -y && apt-get -y install git cmake build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev
```
```bash
./linux-build.sh
```

#### Windows

```bash
windows-build.bat
```

Now copy <code>config.json</code> to <code>../../build</code>, edit it to your liking and run:

```bash
cd ../../build
dotnet MiningCore.dll -c config.json
```

### Building from Source (Visual Studio)

- Install Visual Studio 2017 (Community Edition is sufficient)
- Install the [.Net Core 2.0 SDK](https://www.microsoft.com/net/download/core) for your platform
- Open MiningCore.sln in VS 2017
