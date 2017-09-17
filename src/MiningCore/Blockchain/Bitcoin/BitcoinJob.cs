/* 
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and 
associated documentation files (the "Software"), to deal in the Software without restriction, 
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, 
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, 
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial 
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT 
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE 
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using MiningCore.Blockchain.Bitcoin.DaemonResponses;
using MiningCore.Configuration;
using MiningCore.Crypto;
using MiningCore.Extensions;
using MiningCore.Stratum;
using MiningCore.Util;
using NBitcoin;
using NBitcoin.DataEncoders;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Blockchain.Bitcoin
{
    public class BitcoinJob
    {
        public BitcoinJob(GetBlockTemplateResponse blockTemplate, string jobId,
            PoolConfig poolConfig, ClusterConfig clusterConfig,
            IDestination poolAddressDestination, BitcoinNetworkType networkType,
            BitcoinExtraNonceProvider extraNonceProvider, bool isPoS, double shareMultiplier,
            IHashAlgorithm coinbaseHasher, IHashAlgorithm headerHasher, IHashAlgorithm blockHasher)
        {
            Contract.RequiresNonNull(blockTemplate, nameof(blockTemplate));
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));
            Contract.RequiresNonNull(poolAddressDestination, nameof(poolAddressDestination));
            Contract.RequiresNonNull(extraNonceProvider, nameof(extraNonceProvider));
            Contract.RequiresNonNull(coinbaseHasher, nameof(coinbaseHasher));
            Contract.RequiresNonNull(headerHasher, nameof(headerHasher));
            Contract.RequiresNonNull(blockHasher, nameof(blockHasher));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId), $"{nameof(jobId)} must not be empty");

            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;
            this.poolAddressDestination = poolAddressDestination;
            this.networkType = networkType;
            BlockTemplate = blockTemplate;
            JobId = jobId;

            extraNoncePlaceHolderLength = extraNonceProvider.PlaceHolder.Length;
            this.isPoS = isPoS;
            this.shareMultiplier = shareMultiplier;

            this.coinbaseHasher = coinbaseHasher;
            this.headerHasher = headerHasher;
            this.blockHasher = blockHasher;
        }

        private readonly IHashAlgorithm blockHasher;
        private readonly ClusterConfig clusterConfig;
        private readonly IHashAlgorithm coinbaseHasher;
        private readonly double shareMultiplier;
        private readonly int extraNoncePlaceHolderLength;
        private readonly IHashAlgorithm headerHasher;
        private readonly bool isPoS;

        private readonly BitcoinNetworkType networkType;
        private readonly IDestination poolAddressDestination;
        private readonly PoolConfig poolConfig;
        private readonly HashSet<string> submissions = new HashSet<string>();
        private BigInteger blockTargetValue;
        private byte[] coinbaseFinal;
        private string coinbaseFinalHex;
        private byte[] coinbaseInitial;
        private string coinbaseInitialHex;
        private string[] merkleBranchesHex;
        private MerkleTree mt;

        ///////////////////////////////////////////
        // GetJobParams related properties

        private string previousBlockHashReversedHex;
        private Money rewardToPool;
        private Transaction txOut;

        // serialization constants
        private static byte[] scriptSigFinalBytes = new Script(Op.GetPushOp(Encoding.UTF8.GetBytes("/MiningCore/"))).ToBytes();

        private static byte[] sha256Empty = Enumerable.Repeat((byte) 0, 32).ToArray();
        private static uint txVersion = 1u; // transaction version (currently 1) - see https://en.bitcoin.it/wiki/Transaction

        private static uint txInputCount = 1u;
        private static uint txInPrevOutIndex = (uint) (Math.Pow(2, 32) - 1);
        private static uint txInSequence;
        private static uint txLockTime;

        private void BuildMerkleBranches()
        {
            var transactionHashes = BlockTemplate.Transactions
                .Select(tx => (tx.TxId ?? tx.Hash)
                    .HexToByteArray()
                    .Reverse()
                    .ToArray())
                .ToArray();

            mt = new MerkleTree(transactionHashes);

            merkleBranchesHex = mt.Steps
                .Select(x => x.ToHexString())
                .ToArray();
        }

        private void BuildCoinbase()
        {
            var extraNoncePlaceHolderLengthByte = (byte) extraNoncePlaceHolderLength;

            // generate script parts
            var sigScriptInitial = GenerateScriptSigInitial();
            var sigScriptInitialBytes = sigScriptInitial.ToBytes();

            var sigScriptLength = (uint) (
                sigScriptInitial.Length +
                1 + // for extranonce-placeholder length after sigScriptInitial
                extraNoncePlaceHolderLength +
                scriptSigFinalBytes.Length);

            // output transaction
            txOut = CreateOutputTransaction();

            // build coinbase initial
            using (var stream = new MemoryStream())
            {
                var bs = new BitcoinStream(stream, true);

                // version
                bs.ReadWrite(ref txVersion);

                // timestamp for POS coins
                if (isPoS)
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

                // emit a simulated OP_PUSH(n) just without the payload (which is filled in by the miner: extranonce1 and extranonce2)
                bs.ReadWrite(ref extraNoncePlaceHolderLengthByte);

                // done
                coinbaseInitial = stream.ToArray();
                coinbaseInitialHex = coinbaseInitial.ToHexString();
            }

            // build coinbase final
            using (var stream = new MemoryStream())
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

                // done
                coinbaseFinal = stream.ToArray();
                coinbaseFinalHex = coinbaseFinal.ToHexString();
            }
        }

        private byte[] SerializeOutputTransaction(Transaction tx)
        {
            var withDefaultWitnessCommitment = !string.IsNullOrEmpty(BlockTemplate.DefaultWitnessCommitment);

            var outputCount = (uint) tx.Outputs.Count;
            if (withDefaultWitnessCommitment)
                outputCount++;

            using (var stream = new MemoryStream())
            {
                var bs = new BitcoinStream(stream, true);

                // write output count
                bs.ReadWriteAsVarInt(ref outputCount);

                long amount;
                byte[] raw;
                uint rawLength;

                // serialize witness
                if (withDefaultWitnessCommitment)
                {
                    amount = 0;
                    raw = BlockTemplate.DefaultWitnessCommitment.HexToByteArray();
                    rawLength = (uint) raw.Length;

                    bs.ReadWrite(ref amount);
                    bs.ReadWriteAsVarInt(ref rawLength);
                    bs.ReadWrite(ref raw);
                }

                // serialize other recipients
                foreach (var output in tx.Outputs)
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

        private Script GenerateScriptSigInitial()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); // 1501244088

            // script ops
            var ops = new List<Op>();

            // push block height
            ops.Add(Op.GetPushOp(BlockTemplate.Height));

            // optionally push aux-flags
            if (!string.IsNullOrEmpty(BlockTemplate.CoinbaseAux?.Flags))
                ops.Add(Op.GetPushOp(BlockTemplate.CoinbaseAux.Flags.HexToByteArray()));

            // push timestamp
            ops.Add(Op.GetPushOp(now));

            return new Script(ops);
        }

        private Transaction CreateOutputTransaction()
        {
            var blockReward = new Money(BlockTemplate.CoinbaseValue, MoneyUnit.Satoshi);
            rewardToPool = new Money(BlockTemplate.CoinbaseValue, MoneyUnit.Satoshi);

            var tx = new Transaction();

            // optional DASH stuff
            if (poolConfig.Coin.Type == CoinType.DASH)
                blockReward = CreateDashOutputs(tx, blockReward);

            // Distribute funds to configured reward recipients
            var rewardRecipients = new List<RewardRecipient>(poolConfig.RewardRecipients);

            // Tiny donation to MiningCore developer(s)
            if (!clusterConfig.DisableDevDonation &&
                networkType == BitcoinNetworkType.Main &&
                KnownAddresses.DevFeeAddresses.ContainsKey(poolConfig.Coin.Type))
                rewardRecipients.Add(new RewardRecipient
                {
                    Address = KnownAddresses.DevFeeAddresses[poolConfig.Coin.Type],
                    Percentage = 0.2m
                });

            foreach (var recipient in rewardRecipients.Where(x => x.Percentage > 0))
            {
                var recipientAddress = BitcoinUtils.AddressToScript(recipient.Address);
                var recipientReward = new Money((long) Math.Floor(recipient.Percentage / 100.0m * blockReward.Satoshi));

                rewardToPool -= recipientReward;

                tx.AddOutput(recipientReward, recipientAddress);
            }

            // Finally distribute remaining funds to pool
            tx.Outputs.Insert(0, new TxOut(rewardToPool, poolAddressDestination)
            {
                Value = rewardToPool
            });

            // validate it
            //var checkResult = tx.Check();
            //Debug.Assert(checkResult == TransactionCheckResult.Success);

            return tx;
        }

        private Money CreateDashOutputs(Transaction tx, Money reward)
        {
            if (BlockTemplate.Masternode != null && BlockTemplate.SuperBlocks != null)
            {
                if (!string.IsNullOrEmpty(BlockTemplate.Masternode.Payee))
                {
                    var payeeAddress = BitcoinUtils.AddressToScript(BlockTemplate.Masternode.Payee);
                    var payeeReward = BlockTemplate.Masternode.Amount;

                    reward -= payeeReward;
                    rewardToPool -= payeeReward;

                    tx.AddOutput(payeeReward, payeeAddress);
                }

                else if (BlockTemplate.SuperBlocks.Length > 0)
                {
                    foreach (var superBlock in BlockTemplate.SuperBlocks)
                    {
                        var payeeAddress = BitcoinUtils.AddressToScript(superBlock.Payee);
                        var payeeReward = superBlock.Amount;

                        reward -= payeeReward;
                        rewardToPool -= payeeReward;

                        tx.AddOutput(payeeReward, payeeAddress);
                    }
                }
            }

            if (!string.IsNullOrEmpty(BlockTemplate.Payee))
            {
                var payeeAddress = BitcoinUtils.AddressToScript(BlockTemplate.Payee);
                var payeeReward = BlockTemplate.PayeeAmount ?? (reward / 5);

                reward -= payeeReward;
                rewardToPool -= payeeReward;

                tx.AddOutput(payeeReward, payeeAddress);
            }

            return reward;
        }

        private bool RegisterSubmit(string extraNonce1, string extraNonce2, string nTime, string nonce)
        {
            var key = extraNonce1 + extraNonce2 + nTime + nonce;
            if (submissions.Contains(key))
                return false;

            submissions.Add(key);
            return true;
        }

        private BitcoinShare ProcessShareInternal(string extraNonce1, string extraNonce2, uint nTime, uint nonce, double stratumDifficulty)
        {
            // build coinbase
            var coinbase = SerializeCoinbase(extraNonce1, extraNonce2);
            var coinbaseHash = coinbaseHasher.Digest(coinbase);

            // build merkle-root
            var merkleRoot = mt.WithFirst(coinbaseHash)
                .ToArray();

            // build block-header
            var blockHeader = new BlockHeader
            {
                Version = (int) BlockTemplate.Version,
                Bits = new Target(Encoders.Hex.DecodeData(BlockTemplate.Bits)),
                HashPrevBlock = uint256.Parse(BlockTemplate.PreviousBlockhash),
                HashMerkleRoot = new uint256(merkleRoot),
                BlockTime = DateTimeOffset.FromUnixTimeSeconds(nTime),
                Nonce = nonce
            };

            // hash block-header
            var headerBytes = blockHeader.ToBytes();
            var headerHash = headerHasher.Digest(headerBytes, (ulong) nTime);
            var headerValue = new BigInteger(headerHash);

            // calc share-diff
            var shareDiff = (double) new BigRational(BitcoinConstants.Diff1, headerValue) * shareMultiplier;
            var ratio = shareDiff / stratumDifficulty;

            // test if share meets at least workers current difficulty
            if (ratio < 0.99)
                throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");

            // valid share, check if the share also meets the much harder block difficulty (block candidate)
            var isBlockCandidate = headerValue < blockTargetValue;

            var result = new BitcoinShare
            {
                BlockHeight = BlockTemplate.Height,
                IsBlockCandidate = isBlockCandidate
            };

            var blockBytes = SerializeBlock(headerBytes, coinbase);
            result.BlockHex = blockBytes.ToHexString();
            result.BlockHash = blockHasher.Digest(headerBytes, nTime).ToHexString();
            result.BlockHeight = BlockTemplate.Height;
            result.BlockReward = rewardToPool.ToDecimal(MoneyUnit.BTC);

            return result;
        }

        private byte[] SerializeCoinbase(string extraNonce1, string extraNonce2)
        {
            var extraNonce1Bytes = extraNonce1.HexToByteArray();
            var extraNonce2Bytes = extraNonce2.HexToByteArray();

            using (var stream = new MemoryStream())
            {
                stream.Write(coinbaseInitial);
                stream.Write(extraNonce1Bytes);
                stream.Write(extraNonce2Bytes);
                stream.Write(coinbaseFinal);

                return stream.ToArray();
            }
        }

        private byte[] SerializeBlock(byte[] header, byte[] coinbase)
        {
            var transactionCount = (uint) BlockTemplate.Transactions.Length + 1; // +1 for prepended coinbase tx
            var rawTransactionBuffer = BuildRawTransactionBuffer();

            using (var stream = new MemoryStream())
            {
                var bs = new BitcoinStream(stream, true);

                bs.ReadWrite(ref header);
                bs.ReadWriteAsVarInt(ref transactionCount);
                bs.ReadWrite(ref coinbase);
                bs.ReadWrite(ref rawTransactionBuffer);

                // TODO: handle DASH coin masternode_payments

                // POS coins require a zero byte appended to block which the daemon replaces with the signature
                if (isPoS)
                    bs.ReadWrite((byte) 0);

                return stream.ToArray();
            }
        }

        private byte[] BuildRawTransactionBuffer()
        {
            using (var stream = new MemoryStream())
            {
                foreach (var tx in BlockTemplate.Transactions)
                {
                    var txRaw = tx.Data.HexToByteArray();
                    stream.Write(txRaw);
                }

                return stream.ToArray();
            }
        }

        #region API-Surface

        public GetBlockTemplateResponse BlockTemplate { get; }

        public string JobId { get; }

        public void Init()
        {
            blockTargetValue = BigInteger.Parse(BlockTemplate.Target, NumberStyles.HexNumber);

            previousBlockHashReversedHex = BlockTemplate.PreviousBlockhash
                .HexToByteArray()
                .ReverseByteOrder()
                .ToHexString();

            BuildMerkleBranches();
            BuildCoinbase();
        }

        public object GetJobParams(bool isNew)
        {
            return new object[]
            {
                JobId,
                previousBlockHashReversedHex,
                coinbaseInitialHex,
                coinbaseFinalHex,
                merkleBranchesHex,
                BlockTemplate.Version.ToStringHex8(),
                BlockTemplate.Bits,
                BlockTemplate.CurTime.ToStringHex8(),
                isNew
            };
        }

        public BitcoinShare ProcessShare(string extraNonce1, string extraNonce2, string nTime, string nonce, double stratumDifficulty)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(extraNonce1), $"{nameof(extraNonce1)} must not be empty");
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(extraNonce2), $"{nameof(extraNonce2)} must not be empty");
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nTime), $"{nameof(nTime)} must not be empty");
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce), $"{nameof(nonce)} must not be empty");

            // validate nTime
            if (nTime.Length != 8)
                throw new StratumException(StratumError.Other, "incorrect size of ntime");

            var nTimeInt = uint.Parse(nTime, NumberStyles.HexNumber);
            if (nTimeInt < BlockTemplate.CurTime || nTimeInt > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 7200)
                throw new StratumException(StratumError.Other, "ntime out of range");

            // validate nonce
            if (nonce.Length != 8)
                throw new StratumException(StratumError.Other, "incorrect size of nonce");

            var nonceInt = uint.Parse(nonce, NumberStyles.HexNumber);

            // dupe check
            if (!RegisterSubmit(extraNonce1, extraNonce2, nTime, nonce))
                throw new StratumException(StratumError.DuplicateShare, "duplicate share");

            return ProcessShareInternal(extraNonce1, extraNonce2, nTimeInt, nonceInt, stratumDifficulty);
        }

        #endregion // API-Surface
    }
}
