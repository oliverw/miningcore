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
using System.Numerics;
using MiningCore.Configuration;

namespace MiningCore.Blockchain.Bitcoin
{
    public enum BitcoinNetworkType
    {
        Main = 1,
        Test,
        RegTest
    }

    public enum BitcoinTransactionCategory
    {
        /// <summary>
        /// wallet sending payment
        /// </summary>
        Send = 1,

        /// <summary>
        /// wallet receiving payment in a regular transaction
        /// </summary>
        Receive,

        /// <summary>
        /// matured and spendable coinbase transaction
        /// </summary>
        Generate,

        /// <summary>
        /// coinbase transaction that is not spendable yet
        /// </summary>
        Immature,

        /// <summary>
        /// coinbase transaction from a block that’s not in the local best block chain
        /// </summary>
        Orphan
    }

    public class BitcoinConstants
    {
        public const int ExtranoncePlaceHolderLength = 8;
        public const decimal SatoshisPerBitcoin = 100000000;
        public static double Pow2x32 = Math.Pow(2, 32);
        public static readonly BigInteger Diff1 = BigInteger.Parse("00ffff0000000000000000000000000000000000000000000000000000", NumberStyles.HexNumber);
        public const int CoinbaseMinConfimations = 102;
    }

    public class KnownAddresses
    {
        public static readonly Dictionary<CoinType, string> DevFeeAddresses = new Dictionary<CoinType, string>
        {
            { CoinType.BTC, "17QnVor1B6oK1rWnVVBrdX9gFzVkZZbhDm" },
            { CoinType.LTC, "LTK6CWastkmBzGxgQhTTtCUjkjDA14kxzC" },
            { CoinType.DOGE, "DGDuKRhBewGP1kbUz4hszNd2p6dDzWYy9Q" },
            { CoinType.NMC, "NDSLDpFEcTbuRVcWHdJyiRZThVAcb5Z79o" },
            { CoinType.DGB, "DAFtYMGVdNtqHJoBGg2xqZZwSuYAaEs2Bn" },
            { CoinType.ETH, "0xcb55abBfe361B12323eb952110cE33d5F28BeeE1" },
            { CoinType.ETC, "0xF8cCE9CE143C68d3d4A7e6bf47006f21Cfcf93c0" },
            { CoinType.PPC, "PE8RH6HAvi8sqYg47D58TeKTjyeQFFHWR2" },
            { CoinType.DASH, "XqpBAV9QCaoLnz42uF5frSSfrJTrqHoxjp" },
            { CoinType.VIA, "Vc5rJr2QdA2yo1jBoqYUAH7T59uBh2Vw5q" },
            { CoinType.MONA, "MBbkeAM3VQKg474bgxJEXrtcnMg8cjHY3S" },
            { CoinType.VTC, "VfCAvPVrksYvwcpU7E44e51HxfvVhcxMXf" },
            { CoinType.ZEC, "t1YHZHz2DGVMJiggD2P4fBQ2TAPgtLSUwZ7" },
            { CoinType.BTG, "GQb77ZuMCyJGZFyxpzqNfm7GB1rQreP4n6" }
        };
    }

    public static class BitcoinCommands
    {
        public const string GetBalance = "getbalance";
        public const string GetNetworkInfo = "getnetworkinfo";
        public const string GetMiningInfo = "getmininginfo";
        public const string GetPeerInfo = "getpeerinfo";
        public const string ValidateAddress = "validateaddress";
        public const string GetDifficulty = "getdifficulty";
        public const string GetBlockTemplate = "getblocktemplate";
        public const string GetBlockSubsidy = "getblocksubsidy";
        public const string SubmitBlock = "submitblock";
        public const string GetBlockchainInfo = "getblockchaininfo";
        public const string GetBlock = "getblock";
        public const string GetTransaction = "gettransaction";
        public const string SendMany = "sendmany";
    }
}
