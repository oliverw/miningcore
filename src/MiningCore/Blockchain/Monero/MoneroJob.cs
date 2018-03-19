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
using MiningCore.Blockchain.Monero.DaemonResponses;
using MiningCore.Buffers;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.Native;
using MiningCore.Stratum;
using MiningCore.Util;
using NBitcoin.BouncyCastle.Math;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Blockchain.Monero
{
	public class MoneroJob
	{
		public MoneroJob(GetBlockTemplateResponse blockTemplate, byte[] instanceId, string jobId,
			PoolConfig poolConfig, ClusterConfig clusterConfig)
		{
			Contract.RequiresNonNull(blockTemplate, nameof(blockTemplate));
			Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
			Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));
			Contract.RequiresNonNull(instanceId, nameof(instanceId));
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId), $"{nameof(jobId)} must not be empty");

			switch (poolConfig.Coin.Type)
			{
				case CoinType.AEON:
					hashSlow = (buf, variant)=> LibCryptonote.CryptonightHashSlowLite(buf);
					break;

			    case CoinType.XMR:
                    hashSlow = LibCryptonote.CryptonightHashSlow;
			        break;

                default:
					hashSlow = (buf, variant) => LibCryptonote.CryptonightHashSlow(buf, 0);
					break;
			}

			BlockTemplate = blockTemplate;
			PrepareBlobTemplate(instanceId);
		}

		private readonly Func<byte[], int, PooledArraySegment<byte>> hashSlow;

		private byte[] blobTemplate;
		private uint extraNonce;

		private void PrepareBlobTemplate(byte[] instanceId)
		{
			blobTemplate = BlockTemplate.Blob.HexToByteArray();

			// inject instanceId at the end of the reserved area of the blob
			var destOffset = (int)BlockTemplate.ReservedOffset + MoneroConstants.ExtraNonceSize;
			Buffer.BlockCopy(instanceId, 0, blobTemplate, destOffset, 3);
		}

		private string EncodeBlob(uint workerExtraNonce)
		{
			// clone template
			using (var blob = new PooledArraySegment<byte>(blobTemplate.Length))
			{
				Buffer.BlockCopy(blobTemplate, 0, blob.Array, 0, blobTemplate.Length);

				// inject extranonce (big-endian at the beginning of the reserved area of the blob)
				var extraNonceBytes = BitConverter.GetBytes(workerExtraNonce.ToBigEndian());
				Buffer.BlockCopy(extraNonceBytes, 0, blob.Array, (int)BlockTemplate.ReservedOffset, extraNonceBytes.Length);

				var result = LibCryptonote.ConvertBlob(blob.Array, blobTemplate.Length).ToHexString();
				return result;
			}
		}

		private string EncodeTarget(double difficulty)
		{
			var diff = BigInteger.ValueOf((long)(difficulty * 255d));
			var quotient = MoneroConstants.Diff1.Divide(diff).Multiply(BigInteger.ValueOf(255));
			var bytes = quotient.ToByteArray();
			var padded = Enumerable.Repeat((byte)0, 32).ToArray();

			if (padded.Length - bytes.Length > 0)
				Buffer.BlockCopy(bytes, 0, padded, padded.Length - bytes.Length, bytes.Length);

			var result = new ArraySegment<byte>(padded, 0, 4)
				.Reverse()
				.ToHexString();

			return result;
		}

		private PooledArraySegment<byte> ComputeBlockHash(byte[] blobConverted)
		{
			// blockhash is computed from the converted blob data prefixed with its length
			var bytes = new[] { (byte)blobConverted.Length }
				.Concat(blobConverted)
				.ToArray();

			return LibCryptonote.CryptonightHashFast(bytes);
		}

		#region API-Surface

		public GetBlockTemplateResponse BlockTemplate { get; }

		public void Init()
		{
		}

		public void PrepareWorkerJob(MoneroWorkerJob workerJob, out string blob, out string target)
		{
			workerJob.Height = BlockTemplate.Height;
			workerJob.ExtraNonce = ++extraNonce;

			blob = EncodeBlob(workerJob.ExtraNonce);
			target = EncodeTarget(workerJob.Difficulty);
		}

		public (Share Share, string BlobHex, string BlobHash) ProcessShare(string nonce, uint workerExtraNonce, string workerHash, StratumClient worker)
		{
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce), $"{nameof(nonce)} must not be empty");
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(workerHash), $"{nameof(workerHash)} must not be empty");
			Contract.Requires<ArgumentException>(workerExtraNonce != 0, $"{nameof(workerExtraNonce)} must not be empty");

			var context = worker.GetContextAs<MoneroWorkerContext>();

			// validate nonce
			if (!MoneroConstants.RegexValidNonce.IsMatch(nonce))
				throw new StratumException(StratumError.MinusOne, "malformed nonce");

			// clone template
			using (var blob = new PooledArraySegment<byte>(blobTemplate.Length))
			{
				Buffer.BlockCopy(blobTemplate, 0, blob.Array, 0, blobTemplate.Length);

				// inject extranonce
				var extraNonceBytes = BitConverter.GetBytes(workerExtraNonce.ToBigEndian());
				Buffer.BlockCopy(extraNonceBytes, 0, blob.Array, (int)BlockTemplate.ReservedOffset, extraNonceBytes.Length);

				// inject nonce
				var nonceBytes = nonce.HexToByteArray();
				Buffer.BlockCopy(nonceBytes, 0, blob.Array, MoneroConstants.BlobNonceOffset, nonceBytes.Length);

				// convert
				var blobConverted = LibCryptonote.ConvertBlob(blob.Array, blobTemplate.Length);
				if (blobConverted == null)
					throw new StratumException(StratumError.MinusOne, "malformed blob");

                // PoW variant
			    var hashVariant = blobConverted[0] >= 7 ? blobConverted[0] - 6 : 0;

                // hash it
                using (var hashSeg = hashSlow(blobConverted, hashVariant))
				{
					var hash = hashSeg.ToHexString();
					if (hash != workerHash)
						throw new StratumException(StratumError.MinusOne, "bad hash");

					// check difficulty
					var headerValue = hashSeg.ToBigInteger();
					var shareDiff = (double)new BigRational(MoneroConstants.Diff1b, headerValue);
					var stratumDifficulty = context.Difficulty;
					var ratio = shareDiff / stratumDifficulty;
					var isBlockCandidate = shareDiff >= BlockTemplate.Difficulty;

					// test if share meets at least workers current difficulty
					if (!isBlockCandidate && ratio < 0.99)
					{
						// check if share matched the previous difficulty from before a vardiff retarget
						if (context.VarDiff?.LastUpdate != null && context.PreviousDifficulty.HasValue)
						{
							ratio = shareDiff / context.PreviousDifficulty.Value;

							if (ratio < 0.99)
								throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");

							// use previous difficulty
							stratumDifficulty = context.PreviousDifficulty.Value;
						}

						else
							throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");
					}

					using (var blockHash = ComputeBlockHash(blobConverted))
					{
						var result = new Share
						{
							BlockHeight = BlockTemplate.Height,
							IsBlockCandidate = isBlockCandidate,
                            BlockHash = blockHash.ToHexString(),
                            Difficulty = stratumDifficulty,
						};

					    var blobHex = blob.ToHexString();
					    var blobHash = blockHash.ToHexString();

                        return (result, blobHex, blobHash);
					}
				}
			}
		}

		#endregion // API-Surface
	}
}