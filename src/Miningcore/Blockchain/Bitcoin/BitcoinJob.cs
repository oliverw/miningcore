using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Miningcore.Blockchain.Bitcoin.Configuration;
using Miningcore.Blockchain.Bitcoin.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Crypto;
using Miningcore.Extensions;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;
using Contract = Miningcore.Contracts.Contract;
using Transaction = NBitcoin.Transaction;

namespace Miningcore.Blockchain.Bitcoin
{
    public class BitcoinJob
    {
        protected IHashAlgorithm blockHasher;
        protected IMasterClock clock;
        protected IHashAlgorithm coinbaseHasher;
        protected double shareMultiplier;
        protected int extraNoncePlaceHolderLength;
        protected IHashAlgorithm headerHasher;
        protected bool isPoS;
        protected string txComment;
        protected PayeeBlockTemplateExtra payeeParameters;

        protected Network network;
        protected IDestination poolAddressDestination;
        protected PoolConfig poolConfig;
        protected BitcoinTemplate coin;
        private BitcoinTemplate.BitcoinNetworkParams networkParams;
        protected readonly ConcurrentDictionary<string, bool> submissions = new(StringComparer.OrdinalIgnoreCase);
        protected uint256 blockTargetValue;
        protected byte[] coinbaseFinal;
        protected string coinbaseFinalHex;
        protected byte[] coinbaseInitial;
        protected string coinbaseInitialHex;
        protected string[] merkleBranchesHex;
        protected MerkleTree mt;

        ///////////////////////////////////////////
        // GetJobParams related properties

        protected object[] jobParams;
        protected string previousBlockHashReversedHex;
        protected Money rewardToPool;
        protected Transaction txOut;

        // serialization constants
        protected byte[] scriptSigFinalBytes;

        protected static byte[] sha256Empty = new byte[32];
        protected uint txVersion = 1u; // transaction version (currently 1) - see https://en.bitcoin.it/wiki/Transaction

        protected static uint txInputCount = 1u;
        protected static uint txInPrevOutIndex = (uint) (Math.Pow(2, 32) - 1);
        protected static uint txInSequence;
        protected static uint txLockTime;

        protected virtual void BuildMerkleBranches()
        {
            var transactionHashes = BlockTemplate.Transactions
                .Select(tx => (tx.TxId ?? tx.Hash)
                    .HexToByteArray()
                    .ReverseInPlace())
                .ToArray();

            mt = new MerkleTree(transactionHashes);

            merkleBranchesHex = mt.Steps
                .Select(x => x.ToHexString())
                .ToArray();
        }

        protected virtual void BuildCoinbase()
        {
            // generate script parts
            var sigScriptInitial = GenerateScriptSigInitial();
            var sigScriptInitialBytes = sigScriptInitial.ToBytes();

            var sigScriptLength = (uint) (
                sigScriptInitial.Length +
                extraNoncePlaceHolderLength +
                scriptSigFinalBytes.Length);

            // output transaction
            txOut = CreateOutputTransaction();

            // build coinbase initial
            using(var stream = new MemoryStream())
            {
                var bs = new BitcoinStream(stream, true);

                // version
                bs.ReadWrite(ref txVersion);

                // timestamp for POS coins
                if(isPoS)
                {
                    var timestamp = BlockTemplate.CurTime;
                    bs.ReadWrite(ref timestamp);
                }

                // serialize (simulated) input transaction
                bs.ReadWriteAsVarInt(ref txInputCount);
                bs.ReadWrite(ref sha256Empty);
                bs.ReadWrite(ref txInPrevOutIndex);

                // signature script initial part
                bs.ReadWriteAsVarInt(ref sigScriptLength);
                bs.ReadWrite(ref sigScriptInitialBytes);

                // done
                coinbaseInitial = stream.ToArray();
                coinbaseInitialHex = coinbaseInitial.ToHexString();
            }

            // build coinbase final
            using(var stream = new MemoryStream())
            {
                var bs = new BitcoinStream(stream, true);

                // signature script final part
                bs.ReadWrite(ref scriptSigFinalBytes);

                // tx in sequence
                bs.ReadWrite(ref txInSequence);

                // serialize output transaction
                var txOutBytes = SerializeOutputTransaction(txOut);
                bs.ReadWrite(ref txOutBytes);

                // misc
                bs.ReadWrite(ref txLockTime);

                // Extension point
                AppendCoinbaseFinal(bs);

                // done
                coinbaseFinal = stream.ToArray();
                coinbaseFinalHex = coinbaseFinal.ToHexString();
            }
        }

        protected virtual void AppendCoinbaseFinal(BitcoinStream bs)
        {
            if(!string.IsNullOrEmpty(txComment))
            {
                var data = Encoding.ASCII.GetBytes(txComment);
                bs.ReadWriteAsVarString(ref data);
            }

            if(coin.HasMasterNodes && !string.IsNullOrEmpty(masterNodeParameters.CoinbasePayload))
            {
                var data = masterNodeParameters.CoinbasePayload.HexToByteArray();
                bs.ReadWriteAsVarString(ref data);
            }
        }

        protected virtual byte[] SerializeOutputTransaction(Transaction tx)
        {
            var withDefaultWitnessCommitment = !string.IsNullOrEmpty(BlockTemplate.DefaultWitnessCommitment);

            var outputCount = (uint) tx.Outputs.Count;
            if(withDefaultWitnessCommitment)
                outputCount++;

            using(var stream = new MemoryStream())
            {
                var bs = new BitcoinStream(stream, true);

                // write output count
                bs.ReadWriteAsVarInt(ref outputCount);

                long amount;
                byte[] raw;
                uint rawLength;

                // serialize witness (segwit)
                if(withDefaultWitnessCommitment)
                {
                    amount = 0;
                    raw = BlockTemplate.DefaultWitnessCommitment.HexToByteArray();
                    rawLength = (uint) raw.Length;

                    bs.ReadWrite(ref amount);
                    bs.ReadWriteAsVarInt(ref rawLength);
                    bs.ReadWrite(ref raw);
                }

                // serialize outputs
                foreach(var output in tx.Outputs)
                {
                    amount = output.Value.Satoshi;
                    var outScript = output.ScriptPubKey;
                    raw = outScript.ToBytes(true);
                    rawLength = (uint) raw.Length;

                    bs.ReadWrite(ref amount);
                    bs.ReadWriteAsVarInt(ref rawLength);
                    bs.ReadWrite(ref raw);
                }

                return stream.ToArray();
            }
        }

        protected virtual Script GenerateScriptSigInitial()
        {
            var now = ((DateTimeOffset) clock.Now).ToUnixTimeSeconds();

            // script ops
            var ops = new List<Op>();

            // push block height
            ops.Add(Op.GetPushOp(BlockTemplate.Height));

            // optionally push aux-flags
            if(!coin.CoinbaseIgnoreAuxFlags && !string.IsNullOrEmpty(BlockTemplate.CoinbaseAux?.Flags))
                ops.Add(Op.GetPushOp(BlockTemplate.CoinbaseAux.Flags.HexToByteArray()));

            // push timestamp
            ops.Add(Op.GetPushOp(now));

            // push placeholder
            ops.Add(Op.GetPushOp((uint) 0));

            return new Script(ops);
        }

        protected virtual Transaction CreateOutputTransaction()
        {
            rewardToPool = new Money(BlockTemplate.CoinbaseValue, MoneyUnit.Satoshi);
            var tx = Transaction.Create(network);

            if(coin.HasPayee)
                rewardToPool = CreatePayeeOutput(tx, rewardToPool);

            if(coin.HasMasterNodes)
                rewardToPool = CreateMasternodeOutputs(tx, rewardToPool);

            // Remaining amount goes to pool
            tx.Outputs.Add(rewardToPool, poolAddressDestination);

            return tx;
        }

        protected virtual Money CreatePayeeOutput(Transaction tx, Money reward)
        {
            if(payeeParameters?.PayeeAmount != null && payeeParameters.PayeeAmount.Value > 0)
            {
                var payeeReward = new Money(payeeParameters.PayeeAmount.Value, MoneyUnit.Satoshi);
                reward -= payeeReward;

                tx.Outputs.Add(payeeReward, BitcoinUtils.AddressToDestination(payeeParameters.Payee, network));
            }

            return reward;
        }

        protected bool RegisterSubmit(string extraNonce1, string extraNonce2, string nTime, string nonce)
        {
            var key = new StringBuilder()
                .Append(extraNonce1)
                .Append(extraNonce2) // lowercase as we don't want to accept case-sensitive values as valid.
                .Append(nTime)
                .Append(nonce) // lowercase as we don't want to accept case-sensitive values as valid.
                .ToString();

            return submissions.TryAdd(key, true);
        }

        protected byte[] SerializeHeader(Span<byte> coinbaseHash, uint nTime, uint nonce, uint? versionMask, uint? versionBits)
        {
            // build merkle-root
            var merkleRoot = mt.WithFirst(coinbaseHash.ToArray());

            // Build version
            var version = BlockTemplate.Version;

            // Overt-ASIC boost
            if(versionMask.HasValue && versionBits.HasValue)
                version = (version & ~versionMask.Value) | (versionBits.Value & versionMask.Value);

#pragma warning disable 618
            var blockHeader = new BlockHeader
#pragma warning restore 618
            {
                Version = unchecked((int) version),
                Bits = new Target(Encoders.Hex.DecodeData(BlockTemplate.Bits)),
                HashPrevBlock = uint256.Parse(BlockTemplate.PreviousBlockhash),
                HashMerkleRoot = new uint256(merkleRoot),
                BlockTime = DateTimeOffset.FromUnixTimeSeconds(nTime),
                Nonce = nonce
            };

            return blockHeader.ToBytes();
        }

        protected virtual (Share Share, string BlockHex) ProcessShareInternal(
            StratumConnection worker, string extraNonce2, uint nTime, uint nonce, uint? versionBits)
        {
            var context = worker.ContextAs<BitcoinWorkerContext>();
            var extraNonce1 = context.ExtraNonce1;

            // build coinbase
            var coinbase = SerializeCoinbase(extraNonce1, extraNonce2);
            Span<byte> coinbaseHash = stackalloc byte[32];
            coinbaseHasher.Digest(coinbase, coinbaseHash);

            // hash block-header
            var headerBytes = SerializeHeader(coinbaseHash, nTime, nonce, context.VersionRollingMask, versionBits);
            Span<byte> headerHash = stackalloc byte[32];
            headerHasher.Digest(headerBytes, headerHash, (ulong) nTime, BlockTemplate, coin, networkParams);
            var headerValue = new uint256(headerHash);

            // calc share-diff
            var shareDiff = (double) new BigRational(BitcoinConstants.Diff1, headerHash.ToBigInteger()) * shareMultiplier;
            var stratumDifficulty = context.Difficulty;
            var ratio = shareDiff / stratumDifficulty;

            // check if the share meets the much harder block difficulty (block candidate)
            var isBlockCandidate = headerValue <= blockTargetValue;

            // test if share meets at least workers current difficulty
            if(!isBlockCandidate && ratio < 0.99)
            {
                // check if share matched the previous difficulty from before a vardiff retarget
                if(context.VarDiff?.LastUpdate != null && context.PreviousDifficulty.HasValue)
                {
                    ratio = shareDiff / context.PreviousDifficulty.Value;

                    if(ratio < 0.99)
                        throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");

                    // use previous difficulty
                    stratumDifficulty = context.PreviousDifficulty.Value;
                }

                else
                    throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");
            }

            var result = new Share
            {
                BlockHeight = BlockTemplate.Height,
                NetworkDifficulty = Difficulty,
                Difficulty = stratumDifficulty / shareMultiplier,
            };

            if(isBlockCandidate)
            {
                result.IsBlockCandidate = true;

                Span<byte> blockHash = stackalloc byte[32];
                blockHasher.Digest(headerBytes, blockHash, nTime);
                result.BlockHash = blockHash.ToHexString();

                var blockBytes = SerializeBlock(headerBytes, coinbase);
                var blockHex = blockBytes.ToHexString();

                return (result, blockHex);
            }

            return (result, null);
        }

        protected virtual byte[] SerializeCoinbase(string extraNonce1, string extraNonce2)
        {
            var extraNonce1Bytes = extraNonce1.HexToByteArray();
            var extraNonce2Bytes = extraNonce2.HexToByteArray();

            using(var stream = new MemoryStream())
            {
                stream.Write(coinbaseInitial);
                stream.Write(extraNonce1Bytes);
                stream.Write(extraNonce2Bytes);
                stream.Write(coinbaseFinal);

                return stream.ToArray();
            }
        }

        protected virtual byte[] SerializeBlock(byte[] header, byte[] coinbase)
        {
            var transactionCount = (uint) BlockTemplate.Transactions.Length + 1; // +1 for prepended coinbase tx
            var rawTransactionBuffer = BuildRawTransactionBuffer();

            using(var stream = new MemoryStream())
            {
                var bs = new BitcoinStream(stream, true);

                bs.ReadWrite(ref header);
                bs.ReadWriteAsVarInt(ref transactionCount);
                bs.ReadWrite(ref coinbase);
                bs.ReadWrite(ref rawTransactionBuffer);

                // POS coins require a zero byte appended to block which the daemon replaces with the signature
                if(isPoS)
                    bs.ReadWrite((byte) 0);

                return stream.ToArray();
            }
        }

        protected virtual byte[] BuildRawTransactionBuffer()
        {
            using(var stream = new MemoryStream())
            {
                foreach(var tx in BlockTemplate.Transactions)
                {
                    var txRaw = tx.Data.HexToByteArray();
                    stream.Write(txRaw);
                }

                return stream.ToArray();
            }
        }

        #region Masternodes

        protected MasterNodeBlockTemplateExtra masterNodeParameters;

        protected virtual Money CreateMasternodeOutputs(Transaction tx, Money reward)
        {
            if(masterNodeParameters.Masternode != null)
            {
                Masternode[] masternodes;

                // Dash v13 Multi-Master-Nodes
                if(masterNodeParameters.Masternode.Type == JTokenType.Array)
                    masternodes = masterNodeParameters.Masternode.ToObject<Masternode[]>();
                else
                    masternodes = new[] { masterNodeParameters.Masternode.ToObject<Masternode>() };

                foreach(var masterNode in masternodes)
                {
                    if(!string.IsNullOrEmpty(masterNode.Payee))
                    {
                        var payeeDestination = BitcoinUtils.AddressToDestination(masterNode.Payee, network);
                        var payeeReward = masterNode.Amount;
                        reward -= payeeReward;

                        tx.Outputs.Add(payeeReward, payeeDestination);
                    }
                }
            }

            if(masterNodeParameters.SuperBlocks != null && masterNodeParameters.SuperBlocks.Length > 0)
            {
                foreach(var superBlock in masterNodeParameters.SuperBlocks)
                {
                    var payeeAddress = BitcoinUtils.AddressToDestination(superBlock.Payee, network);
                    var payeeReward = superBlock.Amount;
                    reward -= payeeReward;

                    tx.Outputs.Add(payeeReward, payeeAddress);
                }
            }

            if(!coin.HasPayee && !string.IsNullOrEmpty(masterNodeParameters.Payee))
            {
                var payeeAddress = BitcoinUtils.AddressToDestination(masterNodeParameters.Payee, network);
                var payeeReward = masterNodeParameters.PayeeAmount ?? (reward / 5);

                reward -= payeeReward;

                tx.Outputs.Add(payeeReward, payeeAddress);
            }

            return reward;
        }

        #endregion // Masternodes

        #region API-Surface

        public BlockTemplate BlockTemplate { get; protected set; }
        public double Difficulty { get; protected set; }

        public string JobId { get; protected set; }

        public void Init(BlockTemplate blockTemplate, string jobId,
            PoolConfig poolConfig, BitcoinPoolConfigExtra extraPoolConfig,
            ClusterConfig clusterConfig, IMasterClock clock,
            IDestination poolAddressDestination, Network network,
            bool isPoS, double shareMultiplier, IHashAlgorithm coinbaseHasher,
            IHashAlgorithm headerHasher, IHashAlgorithm blockHasher)
        {
            Contract.RequiresNonNull(blockTemplate, nameof(blockTemplate));
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(poolAddressDestination, nameof(poolAddressDestination));
            Contract.RequiresNonNull(coinbaseHasher, nameof(coinbaseHasher));
            Contract.RequiresNonNull(headerHasher, nameof(headerHasher));
            Contract.RequiresNonNull(blockHasher, nameof(blockHasher));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId), $"{nameof(jobId)} must not be empty");

            this.poolConfig = poolConfig;
            coin = poolConfig.Template.As<BitcoinTemplate>();
            networkParams = coin.GetNetwork(network.ChainName);
            txVersion = coin.CoinbaseTxVersion;
            this.network = network;
            this.clock = clock;
            this.poolAddressDestination = poolAddressDestination;
            BlockTemplate = blockTemplate;
            JobId = jobId;

            var coinbaseString = !string.IsNullOrEmpty(clusterConfig.PaymentProcessing?.CoinbaseString) ?
                clusterConfig.PaymentProcessing?.CoinbaseString.Trim() : "Miningcore";

            scriptSigFinalBytes = new Script(Op.GetPushOp(Encoding.UTF8.GetBytes(coinbaseString))).ToBytes();

            //Difficulty = new Target(new NBitcoin.BouncyCastle.Math.BigInteger(BlockTemplate.Target, 16)).Difficulty;
            Difficulty = new Target(System.Numerics.BigInteger.Parse(BlockTemplate.Target, NumberStyles.HexNumber)).Difficulty;

            extraNoncePlaceHolderLength = BitcoinConstants.ExtranoncePlaceHolderLength;
            this.isPoS = isPoS;
            this.shareMultiplier = shareMultiplier;

            txComment = !string.IsNullOrEmpty(extraPoolConfig?.CoinbaseTxComment) ?
                extraPoolConfig.CoinbaseTxComment : coin.CoinbaseTxComment;

            if(coin.HasMasterNodes)
            {
                masterNodeParameters = BlockTemplate.Extra.SafeExtensionDataAs<MasterNodeBlockTemplateExtra>();

                if(!string.IsNullOrEmpty(masterNodeParameters.CoinbasePayload))
                {
                    txVersion = 3;
                    var txType = 5;
                    txVersion += ((uint) (txType << 16));
                }
            }

            if(coin.HasPayee)
                payeeParameters = BlockTemplate.Extra.SafeExtensionDataAs<PayeeBlockTemplateExtra>();

            this.coinbaseHasher = coinbaseHasher;
            this.headerHasher = headerHasher;
            this.blockHasher = blockHasher;

            if(!string.IsNullOrEmpty(BlockTemplate.Target))
                blockTargetValue = new uint256(BlockTemplate.Target);
            else
            {
                var tmp = new Target(BlockTemplate.Bits.HexToByteArray());
                blockTargetValue = tmp.ToUInt256();
            }

            previousBlockHashReversedHex = BlockTemplate.PreviousBlockhash
                .HexToByteArray()
                .ReverseByteOrder()
                .ToHexString();

            BuildMerkleBranches();
            BuildCoinbase();

            jobParams = new object[]
            {
                JobId,
                previousBlockHashReversedHex,
                coinbaseInitialHex,
                coinbaseFinalHex,
                merkleBranchesHex,
                BlockTemplate.Version.ToStringHex8(),
                BlockTemplate.Bits,
                BlockTemplate.CurTime.ToStringHex8(),
                false
            };
        }

        public object GetJobParams(bool isNew)
        {
            jobParams[^1] = isNew;
            return jobParams;
        }

        public virtual (Share Share, string BlockHex) ProcessShare(StratumConnection worker,
            string extraNonce2, string nTime, string nonce, string versionBits = null)
        {
            Contract.RequiresNonNull(worker, nameof(worker));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(extraNonce2), $"{nameof(extraNonce2)} must not be empty");
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nTime), $"{nameof(nTime)} must not be empty");
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce), $"{nameof(nonce)} must not be empty");

            var context = worker.ContextAs<BitcoinWorkerContext>();

            // validate nTime
            if(nTime.Length != 8)
                throw new StratumException(StratumError.Other, "incorrect size of ntime");

            var nTimeInt = uint.Parse(nTime, NumberStyles.HexNumber);
            if(nTimeInt < BlockTemplate.CurTime || nTimeInt > ((DateTimeOffset) clock.Now).ToUnixTimeSeconds() + 7200)
                throw new StratumException(StratumError.Other, "ntime out of range");

            // validate nonce
            if(nonce.Length != 8)
                throw new StratumException(StratumError.Other, "incorrect size of nonce");

            var nonceInt = uint.Parse(nonce, NumberStyles.HexNumber);

            // validate version-bits (overt ASIC boost)
            uint versionBitsInt = 0;

            if(context.VersionRollingMask.HasValue && versionBits != null)
            {
                versionBitsInt = uint.Parse(versionBits, NumberStyles.HexNumber);

                // enforce that only bits covered by current mask are changed by miner
                if((versionBitsInt & ~context.VersionRollingMask.Value) != 0)
                    throw new StratumException(StratumError.Other, "rolling-version mask violation");
            }

            // dupe check
            if(!RegisterSubmit(context.ExtraNonce1, extraNonce2, nTime, nonce))
                throw new StratumException(StratumError.DuplicateShare, "duplicate share");

            return ProcessShareInternal(worker, extraNonce2, nTimeInt, nonceInt, versionBitsInt);
        }

        #endregion // API-Surface
    }
}
