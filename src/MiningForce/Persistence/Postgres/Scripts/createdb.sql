set role miningforce;

CREATE TABLE shares
(
  id BIGSERIAL NOT NULL PRIMARY KEY,
  coin TEXT NOT NULL,
  blockheight BIGINT NOT NULL,
  difficulty REAL NOT NULL,
  worker TEXT NOT NULL,
  ipaddress TEXT NOT NULL,
  created TIMESTAMP NOT NULL DEFAULT (now()::timestamp at time zone 'utc')
);

CREATE INDEX IDX_COIN_BLOCK_WORKER on shares(coin, blockheight, worker);

CREATE TABLE blocks
(
  id BIGSERIAL NOT NULL PRIMARY KEY,
  coin TEXT NOT NULL,
  blockheight BIGINT NOT NULL,
  status TEXT NOT NULL,
  transactionconfirmationdata TEXT NOT NULL,
  created TIMESTAMP NOT NULL DEFAULT (now()::timestamp at time zone 'utc')
);

CREATE INDEX IDX_BLOCKS_COIN_BLOCK_STATUS_CREATED on blocks(coin, blockheight, status, created);

CREATE TABLE balances
(
  wallet TEXT NOT NULL,
  coin TEXT NOT NULL,
  amount REAL NOT NULL DEFAULT 0,
  created TIMESTAMP NOT NULL DEFAULT (now()::timestamp at time zone 'utc'),
  updated TIMESTAMP NOT NULL DEFAULT (now()::timestamp at time zone 'utc'),

  primary key(wallet, coin)
);
