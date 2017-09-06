## Miningcore

Miningcore is the multi-currency pool-engine powering [poolmining.org](https://poolmining.org)

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

### Building from Source (shell)

Install the [.Net Core 2.0 SDK](https://www.microsoft.com/net/download/core) for your platform 

```bash
git clone https://github.com/coinfoundry/miningcore
cd miningcore
dotnet publish -c Release --framework netcoreapp2.0 -o bin
```
Copy config.json to <code>bin</code>, edit it to your liking and run:

```bash
cd bin
dotnet dotnet MiningCore.dll -c config.json
```

### Building from Source (Visual Studio)

- Install Visual Studio 2017 (Community Edition is sufficient)
- Install the [.Net Core 2.0 SDK](https://www.microsoft.com/net/download/core) for your platform 
- Open MiningCore.sln in VS 2017
