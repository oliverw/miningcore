using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Equihash.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Crypto;
using Miningcore.Crypto.Hashing.Algorithms;
using Miningcore.Crypto.Hashing.Equihash;
using Miningcore.Extensions;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Zcash;

namespace Miningcore.Blockchain.Equihash;

public class EquihashJob
{
    protected IMasterClock clock;
    protected static IHashAlgorithm headerHasher = new Sha256D();
    protected EquihashCoinTemplate coin;
    protected Network network;

    protected IDestination poolAddressDestination;
    protected readonly ConcurrentDictionary<string, bool> submissions = new(StringComparer.OrdinalIgnoreCase);
    protected uint256 blockTargetValue;
    protected byte[] coinbaseInitial;

    protected EquihashCoinTemplate.EquihashNetworkParams networkParams;
    protected decimal blockReward;
    protected decimal rewardFees;

    protected uint txVersionGroupId;
    protected readonly IHashAlgorithm sha256D = new Sha256D();
    protected byte[] coinbaseInitialHash;
    protected byte[] merkleRoot;
    protected byte[] merkleRootReversed;
    protected string merkleRootReversedHex;
    protected EquihashSolver solver;

    // ZCash Sapling & Overwinter support
    protected bool isOverwinterActive;
    protected bool isSaplingActive;

    // temporary reflection hack to force overwinter
    protected static readonly FieldInfo overwinterField = typeof(ZcashTransaction).GetField("fOverwintered", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
    protected static readonly FieldInfo versionGroupField = typeof(ZcashTransaction).GetField("nVersionGroupId", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

    // serialization constants
    protected byte[] sha256Empty = new byte[32];
    protected uint txVersion = 1u; // transaction version (currently 1) - see https://en.bitcoin.it/wiki/Transaction

    ///////////////////////////////////////////
    // GetJobParams related properties

    protected object[] jobParams;
    protected string previousBlockHashReversedHex;
    protected Money rewardToPool;
    protected Transaction txOut;

    protected virtual Transaction CreateOutputTransaction()
    {
        var txNetwork = Network.GetNetwork(networkParams.CoinbaseTxNetwork);
        var tx = Transaction.Create(txNetwork);

        // set versions
        tx.Version = txVersion;

        if(isOverwinterActive)
        {
            overwinterField.SetValue(tx, true);
            versionGroupField.SetValue(tx, txVersionGroupId);
        }

        // calculate outputs
        if(networkParams.PayFundingStream)
        {
            rewardToPool = new Money(Math.Round(blockReward * (1m - (networkParams.PercentFoundersReward) / 100m)) + rewardFees, MoneyUnit.Satoshi);
            tx.Outputs.Add(rewardToPool, poolAddressDestination);

            foreach(FundingStream fundingstream in BlockTemplate.Subsidy.FundingStreams)
            {
                var amount = new Money(Math.Round(fundingstream.ValueZat / 1m), MoneyUnit.Satoshi);
                var destination = FoundersAddressToScriptDestination(fundingstream.Address);
                tx.Outputs.Add(amount, destination);
            }
        }
        else if(networkParams.vOuts)
        {
            rewardToPool = new Money(Math.Round(blockReward * (1m - (networkParams.vPercentFoundersReward) / 100m)) + rewardFees, MoneyUnit.Satoshi);
            tx.Outputs.Add(rewardToPool, poolAddressDestination);
            var destination = FoundersAddressToScriptDestination(networkParams.vTreasuryRewardAddress);
            var amount = new Money(Math.Round(blockReward * (networkParams.vPercentTreasuryReward / 100m)), MoneyUnit.Satoshi);
            tx.Outputs.Add(amount, destination);
            destination = FoundersAddressToScriptDestination(networkParams.vSecureNodesRewardAddress);
            amount = new Money(Math.Round(blockReward * (networkParams.percentSecureNodesReward / 100m)), MoneyUnit.Satoshi);
            tx.Outputs.Add(amount, destination);
            destination = FoundersAddressToScriptDestination(networkParams.vSuperNodesRewardAddress);
            amount = new Money(Math.Round(blockReward * (networkParams.percentSuperNodesReward / 100m)), MoneyUnit.Satoshi);
            tx.Outputs.Add(amount, destination);
        }
        else if(networkParams.PayFoundersReward &&
                (networkParams.LastFoundersRewardBlockHeight >= BlockTemplate.Height ||
                    networkParams.TreasuryRewardStartBlockHeight > 0))
        {
            // founders or treasury reward?
            if(networkParams.TreasuryRewardStartBlockHeight > 0 &&
               BlockTemplate.Height >= networkParams.TreasuryRewardStartBlockHeight)
            {
                // pool reward (t-addr)
                rewardToPool = new Money(Math.Round(blockReward * (1m - (networkParams.PercentTreasuryReward) / 100m)) + rewardFees, MoneyUnit.Satoshi);
                tx.Outputs.Add(rewardToPool, poolAddressDestination);

                // treasury reward (t-addr)
                var destination = FoundersAddressToScriptDestination(GetTreasuryRewardAddress());
                var amount = new Money(Math.Round(blockReward * (networkParams.PercentTreasuryReward / 100m)), MoneyUnit.Satoshi);
                tx.Outputs.Add(amount, destination);
            }

            else
            {
                // pool reward (t-addr)
                rewardToPool = new Money(Math.Round(blockReward * (1m - (networkParams.PercentFoundersReward) / 100m)) + rewardFees, MoneyUnit.Satoshi);
                tx.Outputs.Add(rewardToPool, poolAddressDestination);

                // founders reward (t-addr)
                var destination = FoundersAddressToScriptDestination(GetFoundersRewardAddress());
                var amount = new Money(Math.Round(blockReward * (networkParams.PercentFoundersReward / 100m)), MoneyUnit.Satoshi);
                tx.Outputs.Add(amount, destination);
            }
        }

        else
        {
            // no founders reward
            // pool reward (t-addr)
            rewardToPool = new Money(blockReward + rewardFees, MoneyUnit.Satoshi);
            tx.Outputs.Add(rewardToPool, poolAddressDestination);
        }

        tx.Inputs.Add(TxIn.CreateCoinbase((int) BlockTemplate.Height));

        return tx;
    }

    private string GetTreasuryRewardAddress()
    {
        var index = (int) Math.Floor((BlockTemplate.Height - networkParams.TreasuryRewardStartBlockHeight) /
            networkParams.TreasuryRewardAddressChangeInterval % networkParams.TreasuryRewardAddresses.Length);

        var address = networkParams.TreasuryRewardAddresses[index];
        return address;
    }

    protected virtual void BuildCoinbase()
    {
        // output transaction
        txOut = CreateOutputTransaction();

        using(var stream = new MemoryStream())
        {
            var bs = new ZcashStream(stream, true);
            bs.ReadWrite(ref txOut);

            // done
            coinbaseInitial = stream.ToArray();

            coinbaseInitialHash = new byte[32];
            sha256D.Digest(coinbaseInitial, coinbaseInitialHash);
        }
    }


    protected virtual byte[] SerializeHeader(uint nTime, string nonce)
    {
        var blockHeader = new EquihashBlockHeader
        {
            Version = (int) BlockTemplate.Version,
            Bits = new Target(Encoders.Hex.DecodeData(BlockTemplate.Bits)),
            HashPrevBlock = uint256.Parse(BlockTemplate.PreviousBlockhash),
            HashMerkleRoot = new uint256(merkleRoot),
            NTime = nTime,
            Nonce = nonce
        };

        if(isSaplingActive && !string.IsNullOrEmpty(BlockTemplate.FinalSaplingRootHash))
            blockHeader.HashReserved = BlockTemplate.FinalSaplingRootHash.HexToReverseByteArray();

        return blockHeader.ToBytes();
    }

    private byte[] BuildRawTransactionBuffer()
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

    private byte[] SerializeBlock(Span<byte> header, Span<byte> coinbase, Span<byte> solution)
    {
        var transactionCount = (uint) BlockTemplate.Transactions.Length + 1; // +1 for prepended coinbase tx
        var rawTransactionBuffer = BuildRawTransactionBuffer();

        using(var stream = new MemoryStream())
        {
            var bs = new BitcoinStream(stream, true);

            bs.ReadWrite(ref header);
            bs.ReadWrite(ref solution);
            bs.ReadWriteAsVarInt(ref transactionCount);
            bs.ReadWrite(ref coinbase);
            bs.ReadWrite(ref rawTransactionBuffer);

            return stream.ToArray();
        }
    }

    private (Share Share, string BlockHex) ProcessShareInternal(StratumConnection worker, string nonce,
        uint nTime, string solution)
    {
        var context = worker.ContextAs<BitcoinWorkerContext>();
        var solutionBytes = (Span<byte>) solution.HexToByteArray();

        // serialize block-header
        var headerBytes = SerializeHeader(nTime, nonce);

        // verify solution
        if(!solver.Verify(headerBytes, solutionBytes[networkParams.SolutionPreambleSize..]))
            throw new StratumException(StratumError.Other, "invalid solution");

        // concat header and solution
        Span<byte> headerSolutionBytes = stackalloc byte[headerBytes.Length + solutionBytes.Length];
        headerBytes.CopyTo(headerSolutionBytes);
        solutionBytes.CopyTo(headerSolutionBytes[headerBytes.Length..]);

        // hash block-header
        Span<byte> headerHash = stackalloc byte[32];
        headerHasher.Digest(headerSolutionBytes, headerHash, (ulong) nTime);
        var headerValue = new uint256(headerHash);

        // calc share-diff
        var shareDiff = (double) new BigRational(networkParams.Diff1BValue, headerHash.ToBigInteger());
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
            Difficulty = stratumDifficulty,
        };

        if(isBlockCandidate)
        {
            var headerHashReversed = headerHash.ToNewReverseArray();

            result.IsBlockCandidate = true;
            result.BlockReward = rewardToPool.ToDecimal(MoneyUnit.BTC);
            result.BlockHash = headerHashReversed.ToHexString();

            var blockBytes = SerializeBlock(headerBytes, coinbaseInitial, solutionBytes);
            var blockHex = blockBytes.ToHexString();

            return (result, blockHex);
        }

        return (result, null);
    }

    private bool RegisterSubmit(string nonce, string solution)
    {
        var key = nonce + solution;

        return submissions.TryAdd(key, true);
    }

    #region API-Surface

    public virtual void Init(EquihashBlockTemplate blockTemplate, string jobId,
        PoolConfig poolConfig, ClusterConfig clusterConfig, IMasterClock clock,
        IDestination poolAddressDestination, Network network,
        EquihashSolver solver)
    {
        Contract.RequiresNonNull(blockTemplate);
        Contract.RequiresNonNull(poolConfig);
        Contract.RequiresNonNull(clusterConfig);
        Contract.RequiresNonNull(clock);
        Contract.RequiresNonNull(poolAddressDestination);
        Contract.RequiresNonNull(solver);
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId));

        this.clock = clock;
        this.poolAddressDestination = poolAddressDestination;
        coin = poolConfig.Template.As<EquihashCoinTemplate>();
        networkParams = coin.GetNetwork(network.ChainName);
        this.network = network;
        BlockTemplate = blockTemplate;
        JobId = jobId;
        Difficulty = (double) new BigRational(networkParams.Diff1BValue, BlockTemplate.Target.HexToReverseByteArray().AsSpan().ToBigInteger());

        // ZCash Sapling & Overwinter support
        isSaplingActive = networkParams.SaplingActivationHeight.HasValue &&
            networkParams.SaplingTxVersion.HasValue &&
            networkParams.SaplingTxVersionGroupId.HasValue &&
            networkParams.SaplingActivationHeight.Value > 0 &&
            blockTemplate.Height >= networkParams.SaplingActivationHeight.Value;

        isOverwinterActive = isSaplingActive ||
            networkParams.OverwinterTxVersion.HasValue &&
            networkParams.OverwinterTxVersionGroupId.HasValue &&
            networkParams.OverwinterActivationHeight.HasValue &&
            networkParams.OverwinterActivationHeight.Value > 0 &&
            blockTemplate.Height >= networkParams.OverwinterActivationHeight.Value;

        if(isSaplingActive)
        {
            txVersion = networkParams.SaplingTxVersion.Value;
            txVersionGroupId = networkParams.SaplingTxVersionGroupId.Value;
        }

        else if(isOverwinterActive)
        {
            txVersion = networkParams.OverwinterTxVersion.Value;
            txVersionGroupId = networkParams.OverwinterTxVersionGroupId.Value;
        }

        // Misc
        this.solver = solver;

        if(!string.IsNullOrEmpty(BlockTemplate.Target))
            blockTargetValue = new uint256(BlockTemplate.Target);
        else
        {
            var tmp = new Target(BlockTemplate.Bits.HexToByteArray());
            blockTargetValue = tmp.ToUInt256();
        }

        previousBlockHashReversedHex = BlockTemplate.PreviousBlockhash
            .HexToByteArray()
            .ReverseInPlace()
            .ToHexString();

        if(blockTemplate.Subsidy != null)
            blockReward = blockTemplate.Subsidy.Miner * BitcoinConstants.SatoshisPerBitcoin;
        else
            blockReward = BlockTemplate.CoinbaseValue;

        if(networkParams?.PayFundingStream == true)
        {
            decimal fundingstreamTotal = 0;
            fundingstreamTotal = blockTemplate.Subsidy.FundingStreams.Sum(x => x.Value);
            blockReward = (blockTemplate.Subsidy.Miner + fundingstreamTotal) * BitcoinConstants.SatoshisPerBitcoin;
        }
        else if(networkParams?.vOuts == true)
        {
            blockReward = (decimal) ((blockTemplate.Subsidy.Miner + blockTemplate.Subsidy.Community + blockTemplate.Subsidy.Securenodes + blockTemplate.Subsidy.Supernodes) * BitcoinConstants.SatoshisPerBitcoin);
        }
        else if(networkParams?.PayFoundersReward == true)
        {
            var founders = blockTemplate.Subsidy.Founders ?? blockTemplate.Subsidy.Community;

            if(!founders.HasValue)
                throw new Exception("Error, founders reward missing for block template");

            blockReward = (blockTemplate.Subsidy.Miner + founders.Value) * BitcoinConstants.SatoshisPerBitcoin;
        }

        rewardFees = blockTemplate.Transactions.Sum(x => x.Fee);

        BuildCoinbase();

        // build tx hashes
        var txHashes = new List<uint256> { new(coinbaseInitialHash) };
        txHashes.AddRange(BlockTemplate.Transactions.Select(tx => new uint256(tx.Hash.HexToReverseByteArray())));

        // build merkle root
        merkleRoot = MerkleNode.GetRoot(txHashes).Hash.ToBytes().ReverseInPlace();
        merkleRootReversed = merkleRoot.ReverseInPlace();
        merkleRootReversedHex = merkleRootReversed.ToHexString();

        // misc
        var hashReserved = isSaplingActive && !string.IsNullOrEmpty(blockTemplate.FinalSaplingRootHash) ?
            blockTemplate.FinalSaplingRootHash.HexToReverseByteArray().ToHexString() :
            sha256Empty.ToHexString();

        jobParams = new object[]
        {
            JobId,
            BlockTemplate.Version.ReverseByteOrder().ToStringHex8(),
            previousBlockHashReversedHex,
            merkleRootReversedHex,
            hashReserved,
            BlockTemplate.CurTime.ReverseByteOrder().ToStringHex8(),
            BlockTemplate.Bits.HexToReverseByteArray().ToHexString(),
            false
        };
    }

    public EquihashBlockTemplate BlockTemplate { get; protected set; }
    public double Difficulty { get; protected set; }

    public string JobId { get; protected set; }

    public (Share Share, string BlockHex) ProcessShare(StratumConnection worker, string extraNonce2, string nTime, string solution)
    {
        Contract.RequiresNonNull(worker);
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(extraNonce2));
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nTime));
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(solution));

        var context = worker.ContextAs<BitcoinWorkerContext>();

        // validate nTime
        if(nTime.Length != 8)
            throw new StratumException(StratumError.Other, "incorrect size of ntime");

        var nTimeInt = uint.Parse(nTime.HexToReverseByteArray().ToHexString(), NumberStyles.HexNumber);
        if(nTimeInt < BlockTemplate.CurTime || nTimeInt > ((DateTimeOffset) clock.Now).ToUnixTimeSeconds() + 7200)
            throw new StratumException(StratumError.Other, "ntime out of range");

        var nonce = context.ExtraNonce1 + extraNonce2;

        // validate nonce
        if(nonce.Length != 64)
            throw new StratumException(StratumError.Other, "incorrect size of extraNonce2");

        // validate solution
        if(solution.Length != (networkParams.SolutionSize + networkParams.SolutionPreambleSize) * 2)
            throw new StratumException(StratumError.Other, "incorrect size of solution");

        // dupe check
        if(!RegisterSubmit(nonce, solution))
            throw new StratumException(StratumError.DuplicateShare, "duplicate share");

        return ProcessShareInternal(worker, nonce, nTimeInt, solution);
    }

    public object GetJobParams(bool isNew)
    {
        jobParams[^1] = isNew;
        return jobParams;
    }

    public string GetFoundersRewardAddress()
    {
        var maxHeight = networkParams.LastFoundersRewardBlockHeight;

        var addressChangeInterval = (maxHeight + (ulong) networkParams.FoundersRewardAddresses.Length) / (ulong) networkParams.FoundersRewardAddresses.Length;
        var index = BlockTemplate.Height / addressChangeInterval;

        var address = networkParams.FoundersRewardAddresses[index];
        return address;
    }

    public static IDestination FoundersAddressToScriptDestination(string address)
    {
        var decoded = Encoders.Base58.DecodeData(address);
        var hash = decoded.Skip(2).Take(20).ToArray();
        var result = new ScriptId(hash);
        return result;
    }

    #endregion // API-Surface
}
