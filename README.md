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

### Runtime Requirements

- [.Net Core 2.0 Runtime](https://www.microsoft.com/net/download/core#/runtime)

### Compiling from Source

- Install the [.Net Core 2.0 SDK](https://www.microsoft.com/net/download/core) for your platform 
