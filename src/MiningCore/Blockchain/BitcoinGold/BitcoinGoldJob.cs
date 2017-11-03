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
using System.Linq;
using MiningCore.Blockchain.ZCash;
using MiningCore.Extensions;
using NBitcoin;
using NBitcoin.DataEncoders;
using Transaction = NBitcoin.Transaction;

namespace MiningCore.Blockchain.BitcoinGold
{
    public class BitcoinGoldJob : ZCashJob
    {
	    #region Overrides of ZCashJob

	    protected override Transaction CreateOutputTransaction()
	    {
			rewardToPool = new Money(BlockTemplate.CoinbaseValue, MoneyUnit.Satoshi);

			var tx = new Transaction();

			// pool reward (t-addr)
			var amount = new Money(blockReward + rewardFees, MoneyUnit.Satoshi);
			tx.AddOutput(amount, poolAddressDestination);

			return tx;
		}

	    public override object GetJobParams(bool isNew)
	    {
		    return new object[]
		    {
			    JobId,
			    BlockTemplate.Version.ReverseByteOrder().ToStringHex8(),
			    previousBlockHashReversedHex,
			    merkleRootReversedHex,
			    BlockTemplate.Height.ReverseByteOrder().ToStringHex8() + sha256Empty.Take(28).ToHexString(),	// height + hashReserved
			    BlockTemplate.CurTime.ReverseByteOrder().ToStringHex8(),
			    BlockTemplate.Bits.HexToByteArray().ToReverseArray().ToHexString(),
			    isNew
		    };
	    }

	    protected override byte[] SerializeHeader(uint nTime, string nonce)
	    {
			// BTG requires the blockheight to be encoded in the first 4 bytes of the hashReserved field
		    var heightAndReserved = BitConverter.GetBytes(BlockTemplate.Height)
				.Concat(Enumerable.Repeat((byte) 0, 28))
				.ToArray();

			var blockHeader = new ZCashBlockHeader
		    {
			    Version = (int)BlockTemplate.Version,
			    Bits = new Target(Encoders.Hex.DecodeData(BlockTemplate.Bits)),
			    HashPrevBlock = uint256.Parse(BlockTemplate.PreviousBlockhash),
			    HashMerkleRoot = new uint256(merkleRoot),
			    HashReserved = new uint256(heightAndReserved),
			    NTime = nTime,
			    Nonce = nonce
		    };

		    return blockHeader.ToBytes();
	    }

		#endregion
	}
}
