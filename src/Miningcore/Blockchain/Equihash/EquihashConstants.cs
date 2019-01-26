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

using System.Globalization;
using NBitcoin.Zcash;

namespace Miningcore.Blockchain.Equihash
{
    public class EquihashConstants
    {
        public const int TargetPaddingLength = 32;

        public static readonly System.Numerics.BigInteger ZCashDiff1b =
            System.Numerics.BigInteger.Parse("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.HexNumber);
    }

    public enum ZOperationStatus
    {
        Queued,
        Executing,
        Success,
        Cancelled,
        Failed
    }

    public static class EquihashCommands
    {
        public const string ZGetBalance = "z_getbalance";
        public const string ZGetTotalBalance = "z_gettotalbalance";
        public const string ZGetListAddresses = "z_listaddresses";
        public const string ZValidateAddress = "z_validateaddress";
        public const string ZShieldCoinbase = "z_shieldcoinbase";

        /// <summary>
        /// Returns an operationid. You use the operationid value with z_getoperationstatus and
        /// z_getoperationresult to obtain the result of sending funds, which if successful, will be a txid.
        /// </summary>
        public const string ZSendMany = "z_sendmany";

        public const string ZGetOperationStatus = "z_getoperationstatus";
        public const string ZGetOperationResult = "z_getoperationresult";
    }
}
