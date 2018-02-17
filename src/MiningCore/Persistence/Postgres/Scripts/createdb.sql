set role miningcore;

CREATE TABLE shares
(
	id BIGSERIAL NOT NULL PRIMARY KEY,
	poolid TEXT NOT NULL,
	blockheight BIGINT NOT NULL,
	difficulty DOUBLE PRECISION NOT NULL,
	networkdifficulty DOUBLE PRECISION NOT NULL,
	payoutinfo TEXT NULL,
	miner TEXT NOT NULL,
	worker TEXT NULL,
	useragent TEXT NULL,
	ipaddress TEXT NOT NULL,
	created TIMESTAMP NOT NULL
);

CREATE INDEX IDX_SHARES_POOL_BLOCK on shares(poolid, blockheight);
CREATE INDEX IDX_SHARES_POOL_MINER on shares(poolid, miner);
CREATE INDEX IDX_SHARES_POOL_CREATED ON shares(poolid, created);

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
	id BIGSERIAL NOT NULL PRIMARY KEY,
	poolid TEXT NOT NULL,
	connectedminers INT NOT NULL DEFAULT 0,
	poolhashrate DOUBLE PRECISION NOT NULL DEFAULT 0,
	networkhashrate DOUBLE PRECISION NOT NULL DEFAULT 0,
	networkdifficulty DOUBLE PRECISION NOT NULL DEFAULT 0,
	lastnetworkblocktime TIMESTAMP NULL,
    blockheight BIGINT NOT NULL DEFAULT 0,
    connectedpeers INT NOT NULL DEFAULT 0,
	created TIMESTAMP NOT NULL
);

CREATE INDEX IDX_POOLSTATS_POOL_CREATED on poolstats(poolid, created);
CREATE INDEX IDX_POOLSTATS_POOL_CREATED_HOUR on poolstats(poolid, date_trunc('hour',created));

CREATE TABLE minerstats
(
	id BIGSERIAL NOT NULL PRIMARY KEY,
	poolid TEXT NOT NULL,
	miner TEXT NOT NULL,
	worker TEXT NOT NULL,
	hashrate DOUBLE PRECISION NOT NULL DEFAULT 0,
	sharespersecond DOUBLE PRECISION NOT NULL DEFAULT 0,
	created TIMESTAMP NOT NULL
);

CREATE INDEX IDX_MINERSTATS_POOL_CREATED on minerstats(poolid, created);
CREATE INDEX IDX_MINERSTATS_POOL_MINER_CREATED on minerstats(poolid, miner, created);
CREATE INDEX IDX_MINERSTATS_POOL_MINER_CREATED_HOUR on minerstats(poolid, miner, date_trunc('hour',created));
CREATE INDEX IDX_MINERSTATS_POOL_MINER_CREATED_DAY on minerstats(poolid, miner, date_trunc('day',created));

CREATE TABLE minerstats_pre_agg
(
	poolid TEXT NOT NULL,
	miner TEXT NOT NULL,
	worker TEXT NOT NULL,

 	sharecount BIGINT NOT NULL,
 	sharesaccumulated DOUBLE PRECISION NOT NULL,

	created TIMESTAMP NOT NULL,
	updated TIMESTAMP NOT NULL,

	primary key(poolid, miner, worker)
);

CREATE OR REPLACE FUNCTION aggregate_shares_diff_insert() RETURNS TRIGGER AS
$BODY$
BEGIN
  INSERT INTO minerstats_pre_agg AS d (poolid, miner, worker, sharecount, sharesaccumulated, created, updated)
    VALUES(new.poolid, new.miner, COALESCE(new.worker, ''), 1, new.difficulty, now() at time zone 'utc', now() at time zone 'utc')
  ON CONFLICT ON CONSTRAINT minerstats_pre_agg_pkey
  DO UPDATE SET
    sharecount = d.sharecount + 1, sharesaccumulated = d.sharesaccumulated + new.difficulty, updated = now() at time zone 'utc';

  RETURN new;
END;
$BODY$
language plpgsql;

CREATE TRIGGER TRIG_SHARES_AGGREGATE_DIFF_INSERT
AFTER INSERT ON shares
FOR EACH ROW
EXECUTE PROCEDURE aggregate_shares_diff_insert();

CREATE OR REPLACE FUNCTION aggregate_shares_diff_delete() RETURNS TRIGGER AS
$BODY$
BEGIN
  UPDATE minerstats_pre_agg SET
	sharecount = GREATEST(sharecount - 1, 0), sharesaccumulated = GREATEST(sharesaccumulated - old.difficulty, 0), updated = now() at time zone 'utc'
  WHERE poolid = old.poolid AND miner = old.miner AND worker = COALESCE(old.worker, '');

  RETURN new;
END;
$BODY$
language plpgsql;

CREATE TRIGGER TRIG_SHARES_AGGREGATE_DIFF_DELETE
AFTER DELETE ON shares
FOR EACH ROW
EXECUTE PROCEDURE aggregate_shares_diff_delete();

CREATE OR REPLACE FUNCTION setup_miner_stats_pre_agg()
    RETURNS void AS $$
    DECLARE
      new        record;
    BEGIN
	FOR new IN SELECT sum(difficulty) AS sum, count(difficulty) AS count, poolid, miner, worker from shares group by poolid, miner, worker
	LOOP
	  INSERT INTO minerstats_pre_agg AS d (poolid, miner, worker, sharecount, sharesaccumulated, created, updated)
	    VALUES(new.poolid, new.miner, COALESCE(new.worker, ''), new.count, new.sum, now() at time zone 'utc', now() at time zone 'utc')
	  ON CONFLICT ON CONSTRAINT minerstats_pre_agg_pkey
	  DO UPDATE SET
	    sharecount = new.count, sharesaccumulated = new.sum, updated = now() at time zone 'utc';
	END LOOP;
    END;
$$ LANGUAGE plpgsql;
