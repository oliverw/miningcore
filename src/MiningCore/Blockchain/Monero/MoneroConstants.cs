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
using System.Text.RegularExpressions;
using MiningCore.Configuration;
using MiningCore.Extensions;
using NBitcoin.BouncyCastle.Math;

namespace MiningCore.Blockchain.Monero
{
    public enum MoneroNetworkType
    {
        Main = 1,
        Test
    }

    public class MoneroConstants
    {
        public const string WalletDaemonCategory = "wallet";

        public static readonly Dictionary<CoinType, UInt64> AddressLength = new Dictionary<CoinType, UInt64>
        {
            { CoinType.XMR, 95 },
            { CoinType.ETN, 98 },
            { CoinType.AEON, 97 },
        };

        public static readonly Dictionary<CoinType, UInt64> AddressPrefix = new Dictionary<CoinType, UInt64>
        {
            { CoinType.XMR, 18 },
            { CoinType.ETN, 18018 },
            { CoinType.AEON, 178 },
        };

        public static readonly Dictionary<CoinType, UInt64> AddressPrefixTestnet = new Dictionary<CoinType, UInt64>
        {
            { CoinType.XMR, 53 },
            { CoinType.ETN, 53 },
            { CoinType.AEON, 178 },
        };

        public static readonly Dictionary<CoinType, UInt64> AddressPrefixIntegrated = new Dictionary<CoinType, UInt64>
        {
            { CoinType.XMR, 19 },
            { CoinType.ETN, 18019 },
            { CoinType.AEON, 178 },
        };

        public static readonly Dictionary<CoinType, UInt64> AddressPrefixIntegratedTestnet = new Dictionary<CoinType, UInt64>
        {
            { CoinType.XMR, 54 },
            { CoinType.ETN, 54 },
            { CoinType.AEON, 178 },
        };

        public static readonly Dictionary<CoinType, decimal> SmallestUnit = new Dictionary<CoinType, decimal>
        {
            { CoinType.XMR, Piconero },
            { CoinType.ETN, 100m }, // BUG: https://github.com/electroneum/electroneum/issues/77
            { CoinType.AEON, Piconero },
        };

        public const string DaemonRpcLocation = "json_rpc";
        public const string DaemonRpcDigestAuthRealm = "monero_rpc";
        public const int MoneroRpcMethodNotFound = -32601;
        public const char MainNetAddressPrefix = '4';
        public const char TestNetAddressPrefix = '9';
        public const int PaymentIdHexLength = 64;
        public static readonly Regex RegexValidNonce = new Regex("^[0-9a-f]{8}$", RegexOptions.Compiled);

        public static readonly BigInteger Diff1 = new BigInteger("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", 16);
        public static readonly System.Numerics.BigInteger Diff1b = "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF".HexToByteArray().ToBigInteger();

#if DEBUG
        public const int PayoutMinBlockConfirmations = 2;
#else
        public const int PayoutMinBlockConfirmations = 60;
#endif

        public const int InstanceIdSize = 3;
        public const int ExtraNonceSize = 4;

        // NOTE: for whatever strange reason only reserved_size -1 can be used,
        // the LAST byte MUST be zero or nothing works
        public const int ReserveSize = ExtraNonceSize + InstanceIdSize + 1;

        // Offset to nonce in block blob
        public const int BlobNonceOffset = 39;

        public const long Piconero = (long) 1e12;
        public const decimal StaticTransactionFeeReserve = 0.03m; // in monero
    }

    public static class MoneroCommands
    {
        public const string GetInfo = "get_info";
        public const string GetBlockTemplate = "getblocktemplate";
        public const string SubmitBlock = "submitblock";
        public const string GetBlockHeaderByHash = "getblockheaderbyhash";
        public const string GetBlockHeaderByHeight = "getblockheaderbyheight";
    }

    public static class MoneroWalletCommands
    {
        public const string GetBalance = "getbalance";
        public const string GetAddress = "getaddress";
        public const string Transfer = "transfer";
        public const string TransferSplit = "transfer_split";
        public const string GetTransfers = "get_transfers";
        public const string SplitIntegratedAddress = "split_integrated_address";
    }
}
