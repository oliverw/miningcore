set role miningforce;

CREATE TABLE shares
(
	id BIGSERIAL NOT NULL PRIMARY KEY,
	poolid TEXT NOT NULL,
	blockheight BIGINT NOT NULL,
	difficulty REAL NOT NULL,
	networkdifficulty REAL NOT NULL,
	stratumdifficulty REAL NOT NULL,
	worker TEXT NOT NULL,
	ipaddress TEXT NOT NULL,
	created TIMESTAMP NOT NULL
);

CREATE INDEX IDX_SHARES_POOL_BLOCK on shares(poolid, blockheight);
CREATE INDEX IDX_SHARES_POOL_CREATED on shares(poolid, created);

CREATE TABLE blocks
(
	id BIGSERIAL NOT NULL PRIMARY KEY,
	poolid TEXT NOT NULL,
	blockheight BIGINT NOT NULL,
	status TEXT NOT NULL,
	transactionconfirmationdata TEXT NOT NULL,
	reward decimal(28,12) NULL,
	created TIMESTAMP NOT NULL
);

CREATE INDEX IDX_BLOCKS_POOL_BLOCK_STATUS on blocks(poolid, blockheight, status);

CREATE TABLE balances
(
	poolid TEXT NOT NULL,
	coin TEXT NOT NULL,
	address TEXT NOT NULL,
	amount decimal(28,12) NOT NULL DEFAULT 0,
	created TIMESTAMP NOT NULL,
	updated TIMESTAMP NOT NULL,

	primary key(poolid, address, coin)
);

CREATE TABLE payments
(
	id BIGSERIAL NOT NULL PRIMARY KEY,
	poolid TEXT NOT NULL,
	coin TEXT NOT NULL,
	address TEXT NOT NULL,
	amount decimal(28,12) NOT NULL,
	transactionconfirmationdata TEXT NOT NULL,
	created TIMESTAMP NOT NULL
);

CREATE INDEX IDX_PAYMENTS_POOL_COIN_WALLET on payments(poolid, coin, address);

CREATE TABLE poolstats
(
	poolid TEXT NOT NULL PRIMARY KEY,

	lastpoolblocktime TIMESTAMP NULL,
	connectedminers INT NOT NULL DEFAULT 0,
	poolhashrate FLOAT NOT NULL DEFAULT 0,
	poolfeepercent FLOAT NOT NULL DEFAULT 0,
	donationspercent FLOAT NOT NULL DEFAULT 0,
	sharespersecond FLOAT NOT NULL DEFAULT 0,
	validsharesperminute INT NOT NULL DEFAULT 0,
	invalidsharesperminute INT NOT NULL DEFAULT 0,

	networktype TEXT NOT NULL,
	networkhashrate FLOAT NOT NULL DEFAULT 0,
	lastnetworkblocktime TIMESTAMP NULL,
	networkdifficulty FLOAT NOT NULL DEFAULT 0,
	blockheight INT NOT NULL DEFAULT 0,
	connectedpeers INT NOT NULL DEFAULT 0,
	rewardtype TEXT NOT NULL,

	created TIMESTAMP NOT NULL,
	updated TIMESTAMP NOT NULL
);
