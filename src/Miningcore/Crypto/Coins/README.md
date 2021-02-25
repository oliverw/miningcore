## All Coins Supported in Miningcore
---------------------------------
5G Cash
Actinium
aeon
Alpenstars
auroracoin-groestl
auroracoin-qubit
auroracoin-scrypt
auroracoin-sha256
auroracoin-skein
Axe
Bitcoin
bitcoin-atom
bitcoin-cash
bitcoin-diamond
bitcoin-gold
bitcoin-private
bitcoinsv
bithereum
bittube
browncoin
cannabiscoin
dash
devault
digibyte-groestl
digibyte-odo
digibyte-qubit
digibyte-scrypt
digibyte-sha256
digibyte-skein
dogecoin
elicoin
emark
ethereum
ethereum-classic
fanaticos-cash
feathercoin
flo
freecash
geekcash
globaltoken-scrypt
globaltoken-sha256
goldcash
groestlcoin
help-the-homeless
indexchain
joys-digital
kryptofranc
lanacoin
litecoin
litecoin-cash
luckybit
minexcoin
monacoin
monero
mooncoin
namecoin
networks
note-blockchain
paccoin
pakcoin
peercoin
pigeoncoin
profithunters
pyrk-scrypt
pyrk-sha256
pyrk-x11
ravencoin
rosecoin
shroud
sparkspay
sugarchain
swippcoin
terracoin
tidecoin
titcoin
veles-scrypt
veles-sha256
verge-groestl
verge-lyra
verge-scrypt
verge-x17
vertcoin
VerusCoin
viacoin
xazab
yenten
zcash
zclassic
zencash
zentoshi


#### Ethereum

Miningcore implements the [Ethereum stratum mining protocol](https://github.com/nicehash/Specifications/blob/master/EthereumStratum_NiceHash_v1.0.0.txt) authored by NiceHash. This protocol is implemented by all major Ethereum miners.

- Claymore Miner must be configured to communicate using this protocol by supplying the <code>-esm 3</code> command line option
- Genoil's ethminer must be configured to communicate using this protocol by supplying the <code>-SP 2</code> command line option

#### ZCash

- Pools needs to be configured with both a t-addr and z-addr (new configuration property "z-address" of the pool configuration element)
- First configured zcashd daemon needs to control both the t-addr and the z-addr (have the private key)
- To increase the share processing throughput it is advisable to increase the maximum number of concurrent equihash solvers through the new configuration property "equihashMaxThreads" of the cluster configuration element. Increasing this value by one increases the peak memory consumption of the pool cluster by 1 GB.
- Miners may use both t-addresses and z-addresses when connecting to the pool
