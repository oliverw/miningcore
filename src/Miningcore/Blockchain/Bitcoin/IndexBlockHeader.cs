using NBitcoin.BouncyCastle.Math;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;
using NBitcoin.RPC;
#if !NOJSONNET
using Newtonsoft.Json.Linq;
#endif
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NBitcoin
{
	/// <summary>
	/// Nodes collect new transactions into a block, hash them into a hash tree,
	/// and scan through nonce values to make the block's hash satisfy proof-of-work
	/// requirements.  When they solve the proof-of-work, they broadcast the block
	/// to everyone and the block is added to the block chain.  The first transaction
	/// in the block is a special one that creates a new coin owned by the creator
	/// of the block.
	/// </summary>
	public class IndexBlockHeader : IBitcoinSerializable
	{
		internal const int Size = 81;


		public static IndexBlockHeader Parse(string hex, Network network)
		{
			if (network == null)
				throw new ArgumentNullException(nameof(network));
			return Parse(hex, network.Consensus.ConsensusFactory);
		}

		public static IndexBlockHeader Parse(string hex, Consensus consensus)
		{
			if (consensus == null)
				throw new ArgumentNullException(nameof(consensus));
			return Parse(hex, consensus.ConsensusFactory);
		}

		public static IndexBlockHeader Parse(string hex, ConsensusFactory consensusFactory)
		{
			if (consensusFactory == null)
				throw new ArgumentNullException(nameof(consensusFactory));
			return new IndexBlockHeader(Encoders.Hex.DecodeData(hex), consensusFactory);
		}


		[Obsolete("Use Parse(string hex, Network|Consensus|ConsensusFactory) instead")]
		public static IndexBlockHeader Parse(string hex)
		{
			return Parse(hex, Consensus.Main.ConsensusFactory);
		}

		[Obsolete("You should instantiate IndexBlockHeader from ConsensusFactory.CreateIndexBlockHeader")]
		public IndexBlockHeader()
		{
			SetNull();
		}

		public IndexBlockHeader(string hex, Network network)
			: this(hex, network?.Consensus?.ConsensusFactory ?? throw new ArgumentNullException(nameof(network)))
		{

		}

		public IndexBlockHeader(string hex, Consensus consensus)
			: this(hex, consensus?.ConsensusFactory ?? throw new ArgumentNullException(nameof(consensus)))
		{

		}

		public IndexBlockHeader(string hex, ConsensusFactory consensusFactory)
		{
			if (hex == null)
				throw new ArgumentNullException(nameof(hex));
			if (consensusFactory == null)
				throw new ArgumentNullException(nameof(consensusFactory));
			BitcoinStream bs = new BitcoinStream(Encoders.Hex.DecodeData(hex))
			{
				ConsensusFactory = consensusFactory
			};
			this.ReadWrite(bs);
		}

		[Obsolete("Use new IndexBlockHeader(string hex, Network|Consensus|ConsensusFactory) instead")]
		public IndexBlockHeader(string hex)
			: this(Encoders.Hex.DecodeData(hex))
		{

		}


		public IndexBlockHeader(byte[] data, Network network)
			: this(data, network?.Consensus?.ConsensusFactory ?? throw new ArgumentNullException(nameof(network)))
		{

		}

		public IndexBlockHeader(byte[] data, Consensus consensus)
			: this(data, consensus?.ConsensusFactory ?? throw new ArgumentNullException(nameof(consensus)))
		{

		}

		public IndexBlockHeader(byte[] data, ConsensusFactory consensusFactory)
		{
			if (data == null)
				throw new ArgumentNullException(nameof(data));
			if (consensusFactory == null)
				throw new ArgumentNullException(nameof(consensusFactory));
			BitcoinStream bs = new BitcoinStream(data)
			{
				ConsensusFactory = consensusFactory
			};
			this.ReadWrite(bs);
		}


		[Obsolete("Use new IndexBlockHeader(byte[] hex, Network|Consensus|ConsensusFactory) instead")]
		public IndexBlockHeader(byte[] bytes)
		{
			this.ReadWrite(bytes);
		}


		// header
		const int CURRENT_VERSION = 3;

		protected uint256 hashPrevBlock;

		public uint256 HashPrevBlock
		{
			get
			{
				return hashPrevBlock;
			}
			set
			{
				hashPrevBlock = value;
			}
		}
		protected uint256 hashMerkleRoot;

		protected uint nTime;
		protected uint nBits;

		public Target Bits
		{
			get
			{
				return nBits;
			}
			set
			{
				nBits = value;
			}
		}

		protected int nVersion;

		public int Version
		{
			get
			{
				return nVersion;
			}
			set
			{
				nVersion = value;
			}
		}

		protected uint nNonce;

		public uint Nonce
		{
			get
			{
				return nNonce;
			}
			set
			{
				nNonce = value;
			}
		}
		public uint256 HashMerkleRoot
		{
			get
			{
				return hashMerkleRoot;
			}
			set
			{
				hashMerkleRoot = value;
			}
		}
        protected bool fProofOfStake;
		public bool ProofOfStake
		{
			get
			{
				return fProofOfStake;
			}
			set
			{
				fProofOfStake = value;
			}
		}
		protected byte[] posBlockSig;
			public byte[] PosBlockSig
			{
				get
				{
					return posBlockSig;
				}
				set
				{
					posBlockSig = value;
				}
			}

		protected internal virtual void SetNull()
		{
			nVersion = CURRENT_VERSION;
			hashPrevBlock = 0;
			hashMerkleRoot = 0;
			nTime = 0;
			nBits = 0;
			nNonce = 0;
            fProofOfStake = false;
            posBlockSig = Array.Empty<byte>();
		}

		public virtual bool IsNull
		{
			get
			{
				return (nBits == 0);
			}
		}
		#region IBitcoinSerializable Members

		public virtual void ReadWrite(BitcoinStream stream)
		{
			stream.ReadWrite(ref nVersion);
			stream.ReadWrite(ref hashPrevBlock);
			stream.ReadWrite(ref hashMerkleRoot);
			stream.ReadWrite(ref nTime);
			stream.ReadWrite(ref nBits);
			stream.ReadWrite(ref nNonce);
			stream.ReadWrite(ref fProofOfStake);
			stream.ReadWrite(ref posBlockSig);

		}


		#endregion


		public virtual uint256 GetPoWHash()
		{
			return GetHash();
		}

		public uint256 GetHash()
		{
			uint256 h = null;
			var hashes = _Hashes;
			if (hashes != null)
			{
				h = hashes[0];
			}
			if (h != null)
				return h;

			using (var hs = CreateHashStream())
			{
				var stream = new BitcoinStream(hs, true);
				stream.SerializationTypeScope(SerializationType.Hash);
				this.ReadWrite(stream);
				h = hs.GetHash();
			}

			hashes = _Hashes;
			if (hashes != null)
			{
				hashes[0] = h;
			}
			return h;
		}

		protected virtual HashStreamBase CreateHashStream()
		{
			return new HashStream();
		}

		[Obsolete("Call PrecomputeHash(true, true) instead")]
		public void CacheHashes()
		{
			PrecomputeHash(true, true);
		}

		/// <summary>
		/// Precompute the block header hash so that later calls to GetHash() will returns the precomputed hash
		/// </summary>
		/// <param name="invalidateExisting">If true, the previous precomputed hash is thrown away, else it is reused</param>
		/// <param name="lazily">If true, the hash will be calculated and cached at the first call to GetHash(), else it will be immediately</param>
		public void PrecomputeHash(bool invalidateExisting, bool lazily)
		{
			_Hashes = invalidateExisting ? new uint256[1] : _Hashes ?? new uint256[1];
			if (!lazily && _Hashes[0] == null)
				_Hashes[0] = GetHash();
		}


		uint256[] _Hashes;

		public DateTimeOffset BlockTime
		{
			get
			{
				return Utils.UnixTimeToDateTime(nTime);
			}
			set
			{
				this.nTime = Utils.DateTimeToUnixTime(value);
			}
		}

		static BigInteger Pow256 = BigInteger.ValueOf(2).Pow(256);
		public bool CheckProofOfWork()
		{
			var bits = Bits.ToBigInteger();
			if (bits.CompareTo(BigInteger.Zero) <= 0 || bits.CompareTo(Pow256) >= 0)
				return false;
			// Check proof of work matches claimed amount
			return GetPoWHash() <= Bits.ToUInt256();
		}


	}
}