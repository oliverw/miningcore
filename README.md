[![Build status](https://ci.appveyor.com/api/projects/status/nbvaa55gu3icd1q8?svg=true)](https://ci.appveyor.com/project/oliverw/miningcore)
[![Docker Build Statu](https://img.shields.io/docker/build/coinfoundry/miningcore-docker.svg)](https://hub.docker.com/r/coinfoundry/miningcore-docker/)
[![Docker Stars](https://img.shields.io/docker/stars/coinfoundry/miningcore-docker.svg)](https://hub.docker.com/r/coinfoundry/miningcore-docker/)
[![Docker Pulls](https://img.shields.io/docker/pulls/coinfoundry/miningcore-docker.svg)]()
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
- Payment processing
- Banning System for banning peers that are flooding with invalid shares
- Live Stats API on Port 4000
- POW (proof-of-work) & POS (proof-of-stake) support
- Detailed per-pool logging to console & filesystem
- Runs on Linux and Windows

### Coins

Coin | Implemented | Tested | Planned
:--- | :---: | :---: | :---:
Bitcoin | Yes | Yes |
Bitcoin Cash | Yes | Yes |
Litecoin | Yes | Yes |
Ethereum | Yes | Yes |
Ethereum Classic | Yes | Yes |
Monero | Yes | Yes |
DASH | Yes | Yes |
Groestlcoin | Yes | Yes |
Zcash | No |  | Nov 2017
Dogecoin | Yes | No |
DigiByte | Yes | No |
Namecoin | Yes | No |
Viacoin | Yes | No |
Peercoin | Yes | No |

#### Ethereum

MiningCore implements the [Ethereum stratum mining protocol](https://github.com/nicehash/Specifications/blob/master/EthereumStratum_NiceHash_v1.0.0.txt) authored by NiceHash. This protocol is implemented by all major Ethereum miners.

- Claymore Miner must be configured to speak this protocol by supplying the <code>-esm 3</code> command line option
- Genoil's ethminer must be configured to speak this protocol by supplying the <code>-SP 3</code> command line option

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

### Configuration

MiningCore is configured using a single JSON configuration file which may be used to initialize a cluster of multiple pool each supporting a different crypto-currency.

**Note:** Please refer to [this page](https://github.com/coinfoundry/miningcore/blob/master/src/MiningCore/Configuration/ClusterConfig.cs#L27) for a list of supported coins and other configuration options.

Example configuration:

```javascript
{
  "logging": {
    "level": "info",
    "enableConsoleLog": true,
    "enableConsoleColors": true,
    "logFile": "",
    "logBaseDirectory": "",
    "perPoolLogFile": false
  },
  "banning": {
    "manager": "integrated" // "integrated" or "iptables" (linux only)
  },
  "notifications": {
    "enabled": true,
    "email": {
      "host": "smtp.example.com",
      "port": 587,
      "user": "user",
      "password": "password",
      "fromAddress": "info@yourpool.org",
      "fromName": "pool support"
    },
    "admin": {
      "enabled": false,
      "emailAddress": "user@example.com",
      "notifyBlockFound": true
    }
  },
  // Where to persist shares and blocks to
  "persistence": {
    // Persist to postgresql database
    "postgres": {
      "host": "127.0.0.1",
      "port": 5432,
      "user": "miningcore",
      "password": "yourpassword",
      "database": "miningcore"
    }
  },
  // Donate a tiny percentage of the rewards to the ongoing development of Miningcore
  "devDonation": 0.1,
  // Generate payouts for recorded shares and blocks
  "paymentProcessing": {
    "enabled": true,
    "interval": 600, // how often to process payouts
    // Path to a file used to backup shares under emergency conditions such as database outage
    "shareRecoveryFile": "recovered-shares.txt"
  },
  // Api Settings
  "api": {
    "enabled": true,
    "listenAddress": "127.0.0.1", // Binding address for API, Default: 127.0.0.1
    "port": 4000 // Binding port for API, Default: 4000
  },
  "pools": [{
    // DON'T change the id after a production pool has begun collecting shares!
    "id": "dash1",
    "enabled": true,
    "coin": {
      "type": "DASH"
    },
    // Address to where block rewards are given (pool wallet)
    "address": "yiZodEgQLbYDrWzgBXmfUUHeBVXBNr8rwR",
    // Block rewards go to the configured pool wallet address to later be paid out to miners,
    // except for a percentage that can go to, for examples, pool operator(s) as pool fees or
    // or to donations address. Addresses or hashed public keys can be used. Here is an example
    // of rewards going to the main pool op
    "rewardRecipients": [
      {
        "type": "op",
        "address": "yiZodEgQLbYDrWzgBXmfUUHeBVXBNr8rwR", // pool
        "percentage": 1.5
      }
    ],
    // How often to poll RPC daemons for new blocks, in milliseconds
    "blockRefreshInterval": 1000,
    // Some miner apps will consider the pool dead/offline if it doesn't receive anything new jobs
    // for around a minute, so every time we broadcast jobs, set a timeout to rebroadcast
    // in this many seconds unless we find a new job. Set to zero or remove to disable this.
    "jobRebroadcastTimeout": 55,
    // Some attackers will create thousands of workers that use up all available socket connections,
    // usually the workers are zombies and don't submit shares after connecting. This features
    // detects those and disconnects them.
    "clientConnectionTimeout": 600, // Remove workers that haven't been in contact for this many seconds
    // If a worker is submitting a high threshold of invalid shares we can temporarily ban their IP
    // to reduce system/network load. Also useful to fight against flooding attacks. If running
    // behind something like HAProxy be sure to enable 'tcpProxyProtocol', otherwise you'll end up
    // banning your own IP address (and therefore all workers).
    "banning": {
      "enabled": true,
      "time": 600, // How many seconds to ban worker for
      "invalidPercent": 50, // What percent of invalid shares triggers ban
      "checkThreshold": 50 // Check invalid percent when this many shares have been submitted
    },
    // Each pool can have as many ports for your miners to connect to as you wish. Each port can
    // be configured to use its own pool difficulty and variable difficulty settings. varDiff is
    // optional and will only be used for the ports you configure it for.
    "ports": {
      "3052": { // A port for your miners to connect to
        "listenAddress": "0.0.0.0", // the binding address for the port, default: 127.0.0.1
        "difficulty": 0.02, // the pool difficulty for this port
        // Variable difficulty is a feature that will automatically adjust difficulty for
        // individual miners based on their hashrate in order to lower networking overhead
        "varDiff": {
          "minDiff": 0.01, // Minimum difficulty
          "maxDiff": null, // Network difficulty will be used if it is lower than this (null to disable)
          "targetTime": 15, // Try to get 1 share per this many seconds
          "retargetTime": 90, // Check to see if we should retarget every this many seconds
          "variancePercent": 30 // Allow time to very this % from target without retargeting
          "maxDelta": 500  // Do not alter difficulty by more than this during a single retarget in either direction
        }
      },
      "3053": { //  Another port for your miners to connect to, this port does not use varDiff
        "listenAddress": "0.0.0.0", // the binding address for the port, default: 127.0.0.1
        "difficulty": 100 // 256 //  The pool difficulty
      }
    },
    // Recommended to have at least two daemon instances running in case one drops out-of-sync
    // or offline. For redundancy, all instances will be polled for block/transaction updates
    // and be used for submitting blocks. Creating a backup daemon involves spawning a daemon
    // using the "-datadir=/backup" argument which creates a new daemon instance with it's own
    // RPC config. For more info on this see: https:// en.bitcoin.it/wiki/Data_directory
    // and https:// en.bitcoin.it/wiki/Running_bitcoind
    "daemons": [{
        "host": "127.0.0.1",
        "port": 15001,
        "user": "user",
        "password": "pass"
      }
    ],
    // Generate payouts for recorded shares
    "paymentProcessing": {
      "enabled": true,
      "minimumPayment": 0.01, // in pool-base-currency (ie. Bitcoin, NOT Satoshis)
      "payoutScheme": "PPLNS",
      "payoutSchemeConfig": {
        "factor": 2.0
      }
    }
  }]
}
```

### API

MiningCore exposes a simple REST API at http://localhost:4000. The primary purpose of the API is to power custom web-frontends for the pool.

#### /api/pools

Returns configuration data and current stats for all configured pools.

Example Response:

```javascript
{
  "pools": [{
      "id": "xmr1",
      "coin": {
        "type": "XMR"
      },
      "ports": {
        "4032": {
          "difficulty": 1600.0,
          "varDiff": {
            "minDiff": 1600.0,
            "maxDiff": 160000.0,
            "targetTime": 15.0,
            "retargetTime": 90.0,
            "variancePercent": 30.0
          }
        },
        "4256": {
          "difficulty": 5000.0
        }
      },
      "paymentProcessing": {
        "enabled": true,
        "minimumPayment": 0.01,
        "payoutScheme": "PPLNS",
        "payoutSchemeConfig": {
          "factor": 2.0
        },
        "minimumPaymentToPaymentId": 5.0
      },
      "banning": {
        "enabled": true,
        "checkThreshold": 50,
        "invalidPercent": 50.0,
        "time": 600
      },
      "clientConnectionTimeout": 600,
      "jobRebroadcastTimeout": 55,
      "blockRefreshInterval": 1000,
      "poolFeePercent": 0.0,
      "donationsPercent": 0.0,
      "poolStats": {
        "connectedMiners": 0,
        "poolHashRate": 0.0
      },
      "networkStats": {
        "networkType": "Test",
        "networkHashRate": 39.05,
        "networkDifficulty": 2343.0,
        "lastNetworkBlockTime": "2017-09-17T10:35:55.0394813Z",
        "blockHeight": 157,
        "connectedPeers": 2,
        "rewardType": "POW"
      }
    }]
}
```

#### /api/pool/&lt;poolid&gt;/stats/hourly

Returns pool stats for the last 24 hours (<code>stats</code> array contains 24 entries)

Example Response:

```javascript
{
  "stats": [
    {
      "poolHashRate": 20.0,
      "connectedMiners": 12,
      "created": "2017-09-16T10:00:00"
    },
    {
      "poolHashRate": 25.0,
      "connectedMiners": 15,
      "created": "2017-09-16T11:00:00"
    },

    ...

    {
      "poolHashRate": 23.0,
      "connectedMiners": 13,
      "created": "2017-09-17T10:00:00"
    }
  ]
}
```

#### /api/pool/&lt;poolid&gt;/miner/&lt;miner-wallet-address&gt;/stats

Provides current stats about a miner on a specific pool

Example Response:

```javascript
{
  "pendingShares": 354,
  "pendingBalance": 1.456,
  "totalPaid": 12.354
}
```

#### /api/pool/&lt;poolid&gt;/blocks

Returns information about blocks mined by the pool. Results can be paged by using the <code>page</code> and <code>pageSize</code> query parameters

http://localhost:4000/api/pool/xmr1/blocks?page=2&pageSize=3

Example Response:

```javascript
[
  {
    "blockHeight": 197,
    "status": "pending",
    "transactionConfirmationData": "6e7f68c7891e0f2fdbfd0086d88be3b0d57f1d8f4e1cb78ddc509506e312d94d",
    "reward": 17.558881241740,
    "infoLink": "https://xmrchain.net/block/6e7f68c7891e0f2fdbfd0086d88be3b0d57f1d8f4e1cb78ddc509506e312d94d",
    "created": "2017-09-16T07:41:50.242856"
  },
  {
    "blockHeight": 196,
    "status": "confirmed",
    "transactionConfirmationData": "bb0b42b4936cfa210da7308938ad6d2d34c5339d45b61c750c1e0be2475ec039",
    "reward": 17.558898015821,
    "infoLink": "https://xmrchain.net/block/bb0b42b4936cfa210da7308938ad6d2d34c5339d45b61c750c1e0be2475ec039",
    "created": "2017-09-16T07:41:39.664172"
  },
  {
    "blockHeight": 195,
    "status": "orphaned",
    "transactionConfirmationData": "b9b5943b2646ebfd19311da8031c66b164ace54a7f74ff82556213d9b54daaeb",
    "reward": 17.558914789917,
    "infoLink": "https://xmrchain.net/block/b9b5943b2646ebfd19311da8031c66b164ace54a7f74ff82556213d9b54daaeb",
    "created": "2017-09-16T07:41:14.457664"
  }
]
```

#### /api/pool/&lt;poolid&gt;/payments

Returns information about payments issued by the pool. Results can be paged by using the <code>page</code> and <code>pageSize</code> query parameters

http://localhost:4000/api/pool/xmr1/payments?page=2&pageSize=3

Example Response:

```javascript
[
  {
    "coin": "XMR",
    "address": "9wviCeWe2D8XS82k2ovp5EUYLzBt9pYNW2LXUFsZiv8S3Mt21FZ5qQaAroko1enzw3eGr9qC7X1D7Geoo2RrAotYPwq9Gm8",
    "amount": 7.5354,
    "transactionConfirmationData": "9e7f68c7891e0f2fdbfd0086d88be3b0d57f1d8f4e1cb78ddc509506e312d94d",
    "infoLink": "https://xmrchain.net/tx/9e7f68c7891e0f2fdbfd0086d88be3b0d57f1d8f4e1cb78ddc509506e312d94d",
    "created": "2017-09-16T07:41:50.242856"
  }
]
```

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
