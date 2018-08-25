
[![Docker Build Statu](https://img.shields.io/docker/build/calebcall/miningcore-docker.svg)](https://hub.docker.com/r/calebcall/miningcore-docker/)
[![Docker Stars](https://img.shields.io/docker/stars/calebcall/miningcore-docker.svg)](https://hub.docker.com/r/calebcall/miningcore-docker/)
[![Docker Pulls](https://img.shields.io/docker/pulls/calebcall/miningcore-docker.svg)]()


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

### Algorithms

Algo | Implemented | Tested | Notes
:--- | :---: | :---: | :---:
sha256S  | Yes | Yes |
sha256D | Yes | Yes |
sha256DReverse | Yes | Yes |
x11 | Yes | Yes |
blake2s | Yes | Yes |
x17 | Yes | Yes |
x16r | Yes | Yes |
x16s | Yes | Yes |
groestl | Yes | Yes |
lyra2Rev2 | Yes | Yes |
lyra2z | Yes | Yes |
scrypt | Yes | Yes |
skein | Yes | Yes |
qubit | Yes | Yes |
groestlMyriad | Yes | Yes |
NeoScrypt | Yes | Yes |
DigestReverser(vergeblockhasher) | Yes | Yes |

### Coins

Coin | Implemented | Algorithm | Notes | Website
:--- | :---: | :---: | :---: | :---:
Bitcoin | Yes | sha256 | | https://bitcoin.org
Litecoin | Yes | Scrypt | | https://litecoin.com
Zcash | Yes | Equihash | | https://z.cash
Monero | Yes | Cryptonight | | https://getmonero.org
Ethereum | Yes | Ethash | Requires [Parity](https://github.com/paritytech/parity/releases) | https://www.ethereum.org
Ethereum Classic | Yes | Ethash | Requires [Parity](https://github.com/paritytech/parity/releases) | https://ethereumclassic.org
Expanse | Yes | Ethash | - **Not working for Byzantinium update**<br>- Requires [Parity](https://github.com/paritytech/parity/releases) | http://www.expanse.tech
DASH | Yes | X11 | | https://www.dash.org
Bitcoin Gold | Yes | Equihash | | https://bitcoingold.org
Bitcoin Cash | Yes | sha256 | | https://www.bitcoincash.org
Vertcoin | Yes | Lyra2Rev2 | | http://vertcoin.org
Monacoin | Yes | Lyra2Rev2 | | http://monacoin.org
Globaltoken | Yes | sha256 | Requires [GLT Daemon](https://globaltoken.org/#downloads) | http://globaltoken.org
Ellaism | Yes | Ethash | Requires [Parity](https://github.com/paritytech/parity/releases) | https://ellaism.org
Groestlcoin | Yes | Groestl | | http://www.groestlcoin.org
Dogecoin | Yes | Scrypt | | http://dogecoin.com
DigiByte | Yes | Groestl, Scrypt, skein, sha256, qubit | | http://www.digibyte.io
GoByte | Yes | Neoscrypt | | https://gobyte.network
Bitcoin Private | Yes | Equihash | | https://btcprvate.org/
Horizen | Yes | Equihash | | https://horizen.global
ZClassic | Yes | Equihash | | http://zclassic.org
CannabisCoin | Yes | X11 | | http://cannabiscoin.net
Pakcoin | Yes | Scrypt | | https://www.pakcoin.io
Flocoin | Yes | Scrypt | | https://www.flo.cash
Namecoin | Yes | sha256 | | https://namecoin.org
Viacoin | Yes | Scrypt | | https://viacoin.org
Peercoin | Yes | sha256 | | https://peercoin.net
Straks | Yes | Lyra2Rev2 | | https://straks.tech
Electroneum | Yes | Cryptonight | | https://electroneum.com
MoonCoin | Yes | Scrypt | | http://mooncoin.com
Ravencoin | Yes | X16r | | https://ravencoin.org 
Pigeoncoin | Yes | X16s | | https://pigeoncoin.org
Actinium | Yes | lyra2z | | https://actinium.io
CrowdCoin | Yes | Neoscrypt | | https://crowdcoin.site
Help The Homeless | Yes | X16r | | https://hthcoin.world
Gincoin | Yes | lyra2z | | https://gincoin.io
REDEN | Yes | X16s | | https://www.reden.io

#### Ethereum

Miningcore implements the [Ethereum stratum mining protocol](https://github.com/nicehash/Specifications/blob/master/EthereumStratum_NiceHash_v1.0.0.txt) authored by NiceHash. This protocol is implemented by all major Ethereum miners.

- Claymore Miner must be configured to communicate using this protocol by supplying the <code>-esm 3</code> command line option
- Genoil's ethminer must be configured to communicate using this protocol by supplying the <code>-SP 2</code> command line option

#### ZCash

- Pools needs to be configured with both a t-addr and z-addr (new configuration property "z-address" of the pool configuration element)
- First configured zcashd daemon needs to control both the t-addr and the z-addr (have the private key)
- To increase the share processing throughput it is advisable to increase the maximum number of concurrent equihash solvers through the new configuration property "equihashMaxThreads" of the cluster configuration element. Increasing this value by one increases the peak memory consumption of the pool cluster by 1 GB.
- Miners may use both t-addresses and z-addresses when connecting to the pool

### Runtime Requirements

- [.Net Core 2.0 Runtime](https://www.microsoft.com/net/download/core#/runtime)
- [PostgreSQL Database](https://www.postgresql.org/)
- Coin Daemon (per pool)

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

### [Configuration](https://github.com/calebcall/miningcore/wiki/Configuration)

### [API](https://github.com/coinfoundry/calebcall/wiki/API) 

### Docker

The [miningcore docker image](https://hub.docker.com/r/calebcall/miningcore-docker/) expects a valid pool configuration file as volume argument:

```console
$ docker run -d -p 3032:3032 -p 80:80 -v /path/to/config.json:/config.json:ro calebcall/miningcore-docker
```

You also need to expose all stratum ports specified in your configuration file.  The swagger api documentation will be available on port 80.

### Building from Source (Shell)

Install the [.Net Core 2.0 SDK](https://www.microsoft.com/net/download/core) for your platform

#### Linux (Ubuntu example)

```console
$ apt-get update -y 
$ apt-get -y install git cmake build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev
$ git clone https://github.com/calebcall/miningcore
$ cd miningcore/src/MiningCore
$ ./linux-build.sh
```

#### Windows

```dosbatch
> git clone https://github.com/calebcall/miningcore
> cd miningcore/src/MiningCore
> windows-build.bat
```

#### After successful build

Now copy `config.json` to `../../build`, edit it to your liking and run:

```
cd ../../build
dotnet MiningCore.dll -c config.json
```

### Building from Source (Visual Studio)

- Install [Visual Studio 2017](https://www.visualstudio.com/vs/) (Community Edition is sufficient) for your platform
- Install [.Net Core 2.0 SDK](https://www.microsoft.com/net/download/core) for your platform
- Open `MiningCore.sln` in VS 2017
