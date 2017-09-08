[![Build status](https://ci.appveyor.com/api/projects/status/nbvaa55gu3icd1q8?svg=true)](https://ci.appveyor.com/project/oliverw/miningcore)
[![license](https://img.shields.io/github/license/mashape/apistatus.svg)]()

## MiningCore

MiningCore is the multi-currency pool-engine powering [poolmining.org](https://poolmining.org)

Even though the pool engine can be used to run a production-pool, doing so currently requires to
develop your own website frontend talking to the pool's API-Endpoint at http://127.0.0.1:4000.
This is going to change in the future.

### Features

- Supports clusters of pools each running individual currencies
- Ultra-low-latency Stratum implementation using asynchronous I/O (LibUv)
- Adaptive share difficulty ("vardiff")
- PoW validation (hashing) using native code for maximum performance
- Session management for purging DDoS/flood initiated zombie workers
- Banning System for banning peers that are flooding with invalid shares
- POW (proof-of-work) & POS (proof-of-stake) support
- Detailed per-pool logging to console & filesystem
- Runs on Linux and Windows

### Coins

Coin | Implemented | Tested | Planned
:--- | :---: | :---: | :---:
Monero | Yes | Yes |
Litecoin | Yes | Yes |
Bitcoin | Yes | Yes |
Bitcoin Cash | Yes | Yes |
Zcash | No |  | Oct 2017
Ethereum | No |  | Nov 2017
Ethereum Classic | No |  | Nov 2017
DASH | No |  | Dec 2017
Groestlcoin | Yes | Yes |
Dogecoin | Yes | No |
Einsteinium | Yes | No |
DigiByte | Yes | No |
Namecoin | Yes | No |
Viacoin | Yes | No |

### Runtime Requirements

- [.Net Core 2.0 Runtime](https://www.microsoft.com/net/download/core#/runtime)

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
apt-get update -y && apt-get -y install git cmake build-essential libssl-dev pkg-config libboost-all-dev
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
