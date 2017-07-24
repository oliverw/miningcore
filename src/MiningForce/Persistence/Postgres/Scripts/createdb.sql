set role miningforce;

CREATE TABLE shares
(
  id BIGSERIAL NOT NULL PRIMARY KEY,
  poolid TEXT NOT NULL,
  blockheight BIGINT NOT NULL,
  difficulty REAL NOT NULL,
  networkdifficulty REAL NOT NULL,
  worker TEXT NOT NULL,
  ipaddress TEXT NOT NULL,
  created TIMESTAMP NOT NULL
);

CREATE INDEX IDX_POOL_BLOCK on shares(poolid, blockheight);

CREATE TABLE blocks
(
  id BIGSERIAL NOT NULL PRIMARY KEY,
  poolid TEXT NOT NULL,
  blockheight BIGINT NOT NULL,
  status TEXT NOT NULL,
  transactionconfirmationdata TEXT NOT NULL,
  reward REAL NULL,
  created TIMESTAMP NOT NULL
);

CREATE INDEX IDX_BLOCKS_POOL_BLOCK_STATUS on blocks(poolid, blockheight, status);

CREATE TABLE balances
(
  coin TEXT NOT NULL,
  wallet TEXT NOT NULL,
  amount REAL NOT NULL DEFAULT 0,
  created TIMESTAMP NOT NULL,
  updated TIMESTAMP NOT NULL,

  primary key(wallet, coin)
);

CREATE TABLE payments
(
  id BIGSERIAL NOT NULL PRIMARY KEY,
  poolid TEXT NOT NULL,
  coin TEXT NOT NULL,
  blockheight BIGINT NOT NULL,
  wallet TEXT NOT NULL,
  amount REAL NOT NULL,
  created TIMESTAMP NOT NULL
);

CREATE INDEX IDX_PAYMENTS_POOL_COIN_WALLET on payments(poolid, coin, wallet);
