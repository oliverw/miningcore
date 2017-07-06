////////////////////////
// POOL
    this.start = function(){
        SetupVarDiff();
        SetupApi();
        SetupDaemonInterfaces(function(){
            DetectCoinData(function(){
                SetupRecipients();
                SetupJobManager();
                OnBlockchainSynced(function(){
                    GetFirstJob(function(){
                        SetupBlockPolling();
                        SetupPeer();
                        StartStratumServer(function(){
                            OutputPoolInfo();
                            _this.emit('started');
                        });
                    });
                });
            });
        });
    };

    "blockRefreshInterval": 1000, //How often to poll RPC daemons for new blocks, in milliseconds

    function SetupBlockPolling(){
        if (typeof options.blockRefreshInterval !== "number" || options.blockRefreshInterval <= 0){
            emitLog('Block template polling has been disabled');
            return;
        }

        var pollingInterval = options.blockRefreshInterval;

        blockPollingIntervalId = setInterval(function () {
            GetBlockTemplate(function(error, result, foundNewBlock){
                if (foundNewBlock)
                    emitLog('getting block notification via RPC polling');
            });
        }, pollingInterval);
    }

    function GetBlockTemplate(callback, force){
        gbtParams = [];
        if (_this.options.coin.getblocktemplate == "POS") {
            gbtParams = [{"mode": "template" }];
        } else {
            gbtParams = [{"capabilities": [ "coinbasetxn", "workid", "coinbase/append" ], "rules": [ "segwit" ]}];
        }
        _this.daemon.cmd('getblocktemplate',
            gbtParams,
            function(result){
                if (result.error){
                    emitErrorLog('getblocktemplate call failed for daemon instance ' +
                        result.instance.index + ' with error ' + JSON.stringify(result.error));
                    callback(result.error);
                } else {
                    // Add auxes to the RPC data to process
                    var data = result.response;
                    data.auxes = [];
                    for(var i = 0;i < _this.auxes.length;i++) data.auxes.push(_this.auxes[i].rpcData);
                    var processedNewBlock = _this.jobManager.isNewWork(data);
                    if(processedNewBlock || force) _this.jobManager.processTemplate(data);
                    callback(null, result.response, processedNewBlock);
                    callback = function(){};
                }
            }, true
        );
    }

////////////////////////
// DAEMON

    // This is JsonRpc over http, NOT raw sockets

    /* Sends a JSON RPC (http://json-rpc.org/wiki/specification) command to every configured daemon.
       The callback function is fired once with the result from each daemon unless streamResults is
       set to true. */
    function cmd(method, params, callback, streamResults, returnRawData){

        var results = [];

        async.each(instances, function(instance, eachCallback){

            var itemFinished = function(error, result, data){

                var returnObj = {
                    error: error,
                    response: (result || {}).result,
                    instance: instance
                };
                if (returnRawData) returnObj.data = data;
                if (streamResults) callback(returnObj);
                else results.push(returnObj);
                eachCallback();
                itemFinished = function(){};
            };

            var requestJson = JSON.stringify({
                method: method,
                params: params,
                id: Date.now() + Math.floor(Math.random() * 10)
            });

            performHttpRequest(instance, requestJson, function(error, result, data){
                itemFinished(error, result, data);
            });


        }, function(){
            if (!streamResults){
                callback(results);
            }
        });

    }

////////////////////////
// JOB MANAGER

    this.isNewWork = function(rpcData) {
        /* Block is new if A) its the first block we have seen so far or B) the blockhash is different and the
           block height is greater than the one we have */
        var isNewBlock = typeof(_this.currentJob) === 'undefined';
        if  (!isNewBlock && _this.currentJob.rpcData.previousblockhash !== rpcData.previousblockhash){
            isNewBlock = true;

            //If new block is outdated/out-of-sync than return
            if (rpcData.height < _this.currentJob.rpcData.height)
                return false;
        }

        return isNewBlock;
    }

    //returns true if processed a new block
    this.processTemplate = function(rpcData){
        var auxMerkleTree = buildMerkleTree(rpcData.auxes);

        // MERGED MINING - include merged mining data here
        var tmpBlockTemplate = new blockTemplate(
            jobCounter.next(),
            rpcData,
            options.poolAddressScript,
            _this.extraNoncePlaceholder,
            options.coin.reward,
            options.coin.txMessages,
            options.recipients,
            auxMerkleTree
        );

        this.currentJob = tmpBlockTemplate;

        this.validJobs = {};
        _this.emit('newBlock', tmpBlockTemplate);

        this.validJobs[tmpBlockTemplate.jobId] = tmpBlockTemplate;

        this.auxMerkleTree = auxMerkleTree;

        return true;

    };

////////////////////////
// POOL

    _this.jobManager.on('newBlock', function(blockTemplate){
        //Check if stratumServer has been initialized yet
        if (_this.stratumServer) {
            _this.stratumServer.broadcastMiningJobs(blockTemplate.getJobParams());
        }


////////////////////////
// STRATUM SERVER

    this.broadcastMiningJobs = function(jobParams){
        for (var clientId in stratumClients) {
            var client = stratumClients[clientId];
            client.sendMiningJob(jobParams);
        }
        /* Some miners will consider the pool dead if it doesn't receive a job for around a minute.
           So every time we broadcast jobs, set a timeout to rebroadcast in X seconds unless cleared. */
        clearTimeout(rebroadcastTimeout);
        rebroadcastTimeout = setTimeout(function(){
            _this.emit('broadcastTimeout');
        }, options.jobRebroadcastTimeout * 1000);
    };


////////////////////////
// STRATUM CLIENT

    this.sendMiningJob = function(jobParams){

        var lastActivityAgo = Date.now() - _this.lastActivity;
        if (lastActivityAgo > options.connectionTimeout * 1000){
            _this.emit('socketTimeout', 'last submitted a share was ' + (lastActivityAgo / 1000 | 0) + ' seconds ago');
            _this.socket.destroy();
            return;
        }

        if (pendingDifficulty !== null){
            var result = _this.sendDifficulty(pendingDifficulty);
            pendingDifficulty = null;
            if (result) {
                _this.emit('difficultyChanged', _this.difficulty);
            }
        }
        sendJson({
            id    : null,
            method: "mining.notify",
            params: jobParams
        });
    };


    function handleMessage(message){
        switch(message.method){
            case 'mining.subscribe':
                handleSubscribe(message);
                break;
            case 'mining.authorize':
                handleAuthorize(message, true /*reply to socket*/);
                break;
            case 'mining.get_multiplier':
                _this.emit('log', algos[options.coin.algorithm].multiplier);
                sendJson({
                    id     : null,
                    result : [algos[options.coin.algorithm].multiplier],
                    method : "mining.get_multiplier"
                });
                break;
            case 'ping':
                _this.lastActivity = Date.now();
                sendJson({
                    id     : null,
                    result : [],
                    method : "pong"
                });
                break;
            case 'mining.submit':
                _this.lastActivity = Date.now();
                handleSubmit(message);
                break;
            case 'mining.get_transactions':
                sendJson({
                    id     : null,
                    result : [],
                    error  : true
                });
                break;
            default:
                _this.emit('unknownStratumMethod', message);
                break;
        }
    }
