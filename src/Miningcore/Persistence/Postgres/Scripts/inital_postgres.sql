CREATE USER miningcore WITH ENCRYPTED PASSWORD :pw;
CREATE DATABASE miningcore;
GRANT miningcore TO :su;
ALTER DATABASE miningcore OWNER TO miningcore;
GRANT ALL privileges ON DATABASE miningcore TO miningcore;
