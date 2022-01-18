#!/bin/bash
set -x
set -e

kill -9 $(pgrep -f mockserver) || true

nohup java -jar ./mockserver-netty-5.11.1-jar-with-dependencies.jar -serverPort 1080 > mock-server.txt &
sleep 5

curl -v -X PUT "http://localhost:1080/mockserver/clear" -d '{
    "path" : "/etherscan/api"
}'

curl -v -X PUT "http://localhost:1080/mockserver/expectation" -d '{
  "httpRequest" : {
    "path" : "/etherscan/api",
	"queryStringParameters" : {
      "module" : [ "stats" ],
	  "action" : [ "dailyblkcount" ]
    }
  },
  "httpResponse" : {
    "body" : "{
				   \"status\":\"1\",
				   \"message\":\"OK\",
				   \"result\":[
					  {
						 \"UTCDate\":\"2019-02-01\",
						 \"unixTimeStamp\":\"1548979200\",
						 \"blockCount\":4848,
						 \"blockRewards_Eth\":\"14929.464690870590355682\"
					  },
					  {
						 \"UTCDate\":\"2019-02-28\",
						 \"unixTimeStamp\":\"1551312000\",
						 \"blockCount\":4366,
						 \"blockRewards_Eth\":\"12808.485512162356907132\"
					  }
				   ]
				}"
  }
}'

curl -v -X PUT "http://localhost:1080/mockserver/expectation" -d '{
  "httpRequest" : {
    "path" : "/etherscan/api",
	"queryStringParameters" : {
      "module" : [ "stats" ],
	  "action" : [ "dailyavgblocktime" ]
    }
  },
  "httpResponse" : {
    "body" : "{
			   \"status\":\"1\",
			   \"message\":\"OK\",
			   \"result\":[
				  {
					 \"UTCDate\":\"2019-02-01\",
					 \"unixTimeStamp\":\"1548979200\",
					 \"blockTime_sec\":\"17.67\"
				  },
				  {
					 \"UTCDate\":\"2019-02-28\",
					 \"unixTimeStamp\":\"1551312000\",
					 \"blockTime_sec\":\"19.61\"
				  }
			   ]
			}"
  }
}'