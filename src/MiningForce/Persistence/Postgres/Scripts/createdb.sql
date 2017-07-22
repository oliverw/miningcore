set role miningforce;

CREATE TABLE shares
(
  id BIGSERIAL NOT NULL PRIMARY KEY,
  coin TEXT NOT NULL,
  blockheight BIGINT NOT NULL,
  difficulty real NOT NULL,
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
