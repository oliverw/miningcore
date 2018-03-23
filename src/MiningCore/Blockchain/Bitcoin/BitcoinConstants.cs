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

        public const string ZmqPublisherTopicBlockHash = "hashblock";
        public const string ZmqPublisherTopicTxHash = "hashtx";
        public const string ZmqPublisherTopicBlockRaw = "rawblock";
        public const string ZmqPublisherTopicTxRaw = "rawtx";
    }

    public enum BitcoinRPCErrorCode
    {
        //! Standard JSON-RPC 2.0 errors
        // RPC_INVALID_REQUEST is internally mapped to HTTP_BAD_REQUEST (400).
        // It should not be used for application-layer errors.
        RPC_INVALID_REQUEST = -32600,
        // RPC_METHOD_NOT_FOUND is internally mapped to HTTP_NOT_FOUND (404).
        // It should not be used for application-layer errors.
        RPC_METHOD_NOT_FOUND = -32601,
        RPC_INVALID_PARAMS = -32602,
        // RPC_INTERNAL_ERROR should only be used for genuine errors in bitcoind
        // (for example datadir corruption).
        RPC_INTERNAL_ERROR = -32603,
        RPC_PARSE_ERROR = -32700,

        //! General application defined errors
        RPC_MISC_ERROR = -1,  //!< std::exception thrown in command handling
        RPC_FORBIDDEN_BY_SAFE_MODE = -2,  //!< Server is in safe mode, and command is not allowed in safe mode
        RPC_TYPE_ERROR = -3,  //!< Unexpected type was passed as parameter
        RPC_INVALID_ADDRESS_OR_KEY = -5,  //!< Invalid address or key
        RPC_OUT_OF_MEMORY = -7,  //!< Ran out of memory during operation
        RPC_INVALID_PARAMETER = -8,  //!< Invalid, missing or duplicate parameter
        RPC_DATABASE_ERROR = -20, //!< Database error
        RPC_DESERIALIZATION_ERROR = -22, //!< Error parsing or validating structure in raw format
        RPC_VERIFY_ERROR = -25, //!< General error during transaction or block submission
        RPC_VERIFY_REJECTED = -26, //!< Transaction or block was rejected by network rules
        RPC_VERIFY_ALREADY_IN_CHAIN = -27, //!< Transaction already in chain
        RPC_IN_WARMUP = -28, //!< Client still warming up
        RPC_METHOD_DEPRECATED = -32, //!< RPC method is deprecated

        //! Aliases for backward compatibility
        RPC_TRANSACTION_ERROR = RPC_VERIFY_ERROR,
        RPC_TRANSACTION_REJECTED = RPC_VERIFY_REJECTED,
        RPC_TRANSACTION_ALREADY_IN_CHAIN = RPC_VERIFY_ALREADY_IN_CHAIN,

        //! P2P client errors
        RPC_CLIENT_NOT_CONNECTED = -9,  //!< Bitcoin is not connected
        RPC_CLIENT_IN_INITIAL_DOWNLOAD = -10, //!< Still downloading initial blocks
        RPC_CLIENT_NODE_ALREADY_ADDED = -23, //!< Node is already added
        RPC_CLIENT_NODE_NOT_ADDED = -24, //!< Node has not been added before
        RPC_CLIENT_NODE_NOT_CONNECTED = -29, //!< Node to disconnect not found in connected nodes
        RPC_CLIENT_INVALID_IP_OR_SUBNET = -30, //!< Invalid IP/Subnet
        RPC_CLIENT_P2P_DISABLED = -31, //!< No valid connection manager instance found

        //! Wallet errors
        RPC_WALLET_ERROR = -4,  //!< Unspecified problem with wallet (key not found etc.)
        RPC_WALLET_INSUFFICIENT_FUNDS = -6,  //!< Not enough funds in wallet or account
        RPC_WALLET_INVALID_ACCOUNT_NAME = -11, //!< Invalid account name
        RPC_WALLET_KEYPOOL_RAN_OUT = -12, //!< Keypool ran out, call keypoolrefill first
        RPC_WALLET_UNLOCK_NEEDED = -13, //!< Enter the wallet passphrase with walletpassphrase first
        RPC_WALLET_PASSPHRASE_INCORRECT = -14, //!< The wallet passphrase entered was incorrect
        RPC_WALLET_WRONG_ENC_STATE = -15, //!< Command given in wrong wallet encryption state (encrypting an encrypted wallet etc.)
        RPC_WALLET_ENCRYPTION_FAILED = -16, //!< Failed to encrypt the wallet
        RPC_WALLET_ALREADY_UNLOCKED = -17, //!< Wallet is already unlocked
        RPC_WALLET_NOT_FOUND = -18, //!< Invalid wallet specified
        RPC_WALLET_NOT_SPECIFIED = -19, //!< No wallet specified (error when there are multiple wallets loaded)
    }

    public class DevDonation
    {
        public const decimal Percent = 0.1m;

        public static readonly Dictionary<CoinType, string> Addresses = new Dictionary<CoinType, string>
        {
            {CoinType.BTC, "17QnVor1B6oK1rWnVVBrdX9gFzVkZZbhDm"},
            {CoinType.BCH, "1LJGTzNDTuTvkHpTxNSdmAEBAXAnEHDVqQ"},
            {CoinType.LTC, "LTK6CWastkmBzGxgQhTTtCUjkjDA14kxzC"},
            {CoinType.DOGE, "DGDuKRhBewGP1kbUz4hszNd2p6dDzWYy9Q"},
            {CoinType.NMC, "NDSLDpFEcTbuRVcWHdJyiRZThVAcb5Z79o"},
            {CoinType.DGB, "DAFtYMGVdNtqHJoBGg2xqZZwSuYAaEs2Bn"},
            {CoinType.ETH, "0xcb55abBfe361B12323eb952110cE33d5F28BeeE1"},
            {CoinType.ETC, "0xF8cCE9CE143C68d3d4A7e6bf47006f21Cfcf93c0"},
            {CoinType.PPC, "PE8RH6HAvi8sqYg47D58TeKTjyeQFFHWR2"},
            {CoinType.DASH, "XqpBAV9QCaoLnz42uF5frSSfrJTrqHoxjp"},
            {CoinType.VIA, "Vc5rJr2QdA2yo1jBoqYUAH7T59uBh2Vw5q"},
            {CoinType.MONA, "MBbkeAM3VQKg474bgxJEXrtcnMg8cjHY3S"},
            {CoinType.VTC, "VwDWBHzhYeuyMcHpaZ5nZryggUjHSxUKKK"},
            {CoinType.ZEC, "t1YHZHz2DGVMJiggD2P4fBQ2TAPgtLSUwZ7"},
            {CoinType.ZCL, "t1MFU1vD3YKgsK6Uh8hW7UTY8mKAV2xVqBr"},
            {CoinType.ZEN, "znigQacfTvRiwD2TRhwkBHLNchQ2AZisD94"},
            {CoinType.BTG, "GRao6KHQ8a4GUjAZRVbeCLfRbSkJQQaeMg"},
            {CoinType.MOON, "2QvpGimMYLyqKsczQXZjv56h6me3M8orwj" },
            {CoinType.XVG, "D5xPoHLM6HPkwWSqAweECTSQirJBmRjS8i" },
            {CoinType.XMR, "475YVJbPHPedudkhrcNp1wDcLMTGYusGPF5fqE7XjnragVLPdqbCHBdZg3dF4dN9hXMjjvGbykS6a77dTAQvGrpiQqHp2eH"},
            {CoinType.ETN, "etnkQJwBCjmR1MfkU8D355ZWxxLMhs8miPrtKHWN4U3uUowq9ugeKccVBoEG3n13n74us5AkT8tEoTog46w4HBgn8sMuBRhm9h"},
        };
    }

    public static class BitcoinCommands
    {
        public const string GetBalance = "getbalance";
        public const string ListUnspent = "listunspent";
        public const string GetNetworkInfo = "getnetworkinfo";
        public const string GetMiningInfo = "getmininginfo";
        public const string GetPeerInfo = "getpeerinfo";
        public const string ValidateAddress = "validateaddress";
        public const string GetBlockTemplate = "getblocktemplate";
        public const string GetBlockSubsidy = "getblocksubsidy";
        public const string SubmitBlock = "submitblock";
        public const string GetBlockchainInfo = "getblockchaininfo";
        public const string GetBlock = "getblock";
        public const string GetTransaction = "gettransaction";
        public const string SendMany = "sendmany";
        public const string WalletPassphrase = "walletpassphrase";
        public const string WalletLock = "walletlock";

        // Legacy commands
        public const string GetInfo = "getinfo";
        public const string GetDifficulty = "getdifficulty";
        public const string GetConnectionCount = "getconnectioncount";
    }
}
