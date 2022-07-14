SET ROLE miningcore;

CREATE TABLE shares
(
	poolid TEXT NOT NULL,
	blockheight BIGINT NOT NULL,
	difficulty DOUBLE PRECISION NOT NULL,
	networkdifficulty DOUBLE PRECISION NOT NULL,
	miner TEXT NOT NULL,
	worker TEXT NULL,
	useragent TEXT NULL,
	ipaddress TEXT NOT NULL,
    source TEXT NULL,
	created TIMESTAMPTZ NOT NULL
);

CREATE INDEX IDX_SHARES_POOL_MINER on shares(poolid, miner);
CREATE INDEX IDX_SHARES_POOL_CREATED ON shares(poolid, created);
CREATE INDEX IDX_SHARES_POOL_MINER_DIFFICULTY on shares(poolid, miner, difficulty);

CREATE TABLE blocks
(
	id BIGSERIAL NOT NULL PRIMARY KEY,
	poolid TEXT NOT NULL,
	blockheight BIGINT NOT NULL,
	networkdifficulty DOUBLE PRECISION NOT NULL,
	status TEXT NOT NULL,
    type TEXT NULL,
    confirmationprogress FLOAT NOT NULL DEFAULT 0,
	effort FLOAT NULL,
	transactionconfirmationdata TEXT NOT NULL,
	miner TEXT NULL,
	reward decimal(28,12) NULL,
    source TEXT NULL,
    hash TEXT NULL,
	created TIMESTAMPTZ NOT NULL,

    CONSTRAINT BLOCKS_POOL_HEIGHT UNIQUE (poolid, blockheight, type) DEFERRABLE INITIALLY DEFERRED
);

CREATE INDEX IDX_BLOCKS_POOL_BLOCK_STATUS on blocks(poolid, blockheight, status);

CREATE TABLE balances
(
	poolid TEXT NOT NULL,
	address TEXT NOT NULL,
	amount decimal(28,12) NOT NULL DEFAULT 0,
	created TIMESTAMPTZ NOT NULL,
	updated TIMESTAMPTZ NOT NULL,

	primary key(poolid, address)
);

CREATE TABLE balance_changes
(
	id BIGSERIAL NOT NULL PRIMARY KEY,
	poolid TEXT NOT NULL,
	address TEXT NOT NULL,
	amount decimal(28,12) NOT NULL DEFAULT 0,
	usage TEXT NULL,
    tags text[] NULL,
	created TIMESTAMPTZ NOT NULL
);

CREATE INDEX IDX_BALANCE_CHANGES_POOL_ADDRESS_CREATED on balance_changes(poolid, address, created desc);
CREATE INDEX IDX_BALANCE_CHANGES_POOL_TAGS on balance_changes USING gin (tags);

CREATE TABLE miner_settings
(
	poolid TEXT NOT NULL,
	address TEXT NOT NULL,
	paymentthreshold decimal(28,12) NOT NULL,
	created TIMESTAMPTZ NOT NULL,
	updated TIMESTAMPTZ NOT NULL,

	primary key(poolid, address)
);

CREATE TABLE payments
(
	id BIGSERIAL NOT NULL PRIMARY KEY,
	poolid TEXT NOT NULL,
	coin TEXT NOT NULL,
	address TEXT NOT NULL,
	amount decimal(28,12) NOT NULL,
	transactionconfirmationdata TEXT NOT NULL,
	created TIMESTAMPTZ NOT NULL
);

CREATE INDEX IDX_PAYMENTS_POOL_COIN_WALLET on payments(poolid, coin, address);

CREATE TABLE poolstats
(
	id BIGSERIAL NOT NULL PRIMARY KEY,
	poolid TEXT NOT NULL,
	connectedminers INT NOT NULL DEFAULT 0,
	poolhashrate DOUBLE PRECISION NOT NULL DEFAULT 0,
	sharespersecond DOUBLE PRECISION NOT NULL DEFAULT 0,
	networkhashrate DOUBLE PRECISION NOT NULL DEFAULT 0,
	networkdifficulty DOUBLE PRECISION NOT NULL DEFAULT 0,
	lastnetworkblocktime TIMESTAMPTZ NULL,
    blockheight BIGINT NOT NULL DEFAULT 0,
    connectedpeers INT NOT NULL DEFAULT 0,
	created TIMESTAMPTZ NOT NULL
);

CREATE INDEX IDX_POOLSTATS_POOL_CREATED on poolstats(poolid, created);

CREATE TABLE minerstats
(
	id BIGSERIAL NOT NULL PRIMARY KEY,
	poolid TEXT NOT NULL,
	miner TEXT NOT NULL,
	worker TEXT NOT NULL,
	hashrate DOUBLE PRECISION NOT NULL DEFAULT 0,
	sharespersecond DOUBLE PRECISION NOT NULL DEFAULT 0,
	created TIMESTAMPTZ NOT NULL
);

CREATE INDEX IDX_MINERSTATS_POOL_CREATED on minerstats(poolid, created);
CREATE INDEX IDX_MINERSTATS_POOL_MINER_CREATED on minerstats(poolid, miner, created);
CREATE INDEX IDX_MINERSTATS_POOL_MINER_WORKER_CREATED_HASHRATE on minerstats(poolid,miner,worker,created desc,hashrate);
