using System;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace MiningCore.Blockchain.ZCash
{
    public class ZCashBlockHeader : IBitcoinSerializable
    {
        public ZCashBlockHeader(string hex)
            : this(Encoders.Hex.DecodeData(hex))
        {
        }

        public ZCashBlockHeader(byte[] bytes)
        {
            this.ReadWrite(bytes);
        }

        public ZCashBlockHeader()
        {
            SetNull();
        }

        private uint256 hashMerkleRoot;
        private uint256 hashPrevBlock;
        private uint256 hashReserved;
        private uint nBits;
        private uint nNonce;
        private uint nTime;
        private int nVersion;
	    private byte[] nSolution;

        // header
        private const int CURRENT_VERSION = 4;

        public uint256 HashPrevBlock
        {
            get => hashPrevBlock;
            set => hashPrevBlock = value;
        }

        public Target Bits
        {
            get => nBits;
            set => nBits = value;
        }

        public int Version
        {
            get => nVersion;
            set => nVersion = value;
        }

        public uint Nonce
        {
            get => nNonce;
            set => nNonce = value;
        }

        public uint256 HashMerkleRoot
        {
            get => hashMerkleRoot;
            set => hashMerkleRoot = value;
        }

        public bool IsNull => nBits == 0;

        public DateTimeOffset BlockTime
        {
            get => Utils.UnixTimeToDateTime(nTime);
            set => nTime = Utils.DateTimeToUnixTime(value);
        }

	    public byte[] Solution
	    {
		    get => nSolution;
		    set => nSolution = value;
	    }

        #region IBitcoinSerializable Members

		public void ReadWrite(BitcoinStream stream)
        {
			var solutionLength = (uint) nSolution.Length;

			stream.ReadWrite(ref nVersion);
            stream.ReadWrite(ref hashPrevBlock);
            stream.ReadWrite(ref hashMerkleRoot);
            stream.ReadWrite(ref hashReserved);
            stream.ReadWrite(ref nTime);
            stream.ReadWrite(ref nBits);
            stream.ReadWrite(ref nNonce);
	        stream.ReadWriteAsCompactVarInt(ref solutionLength);
			stream.ReadWrite(ref nSolution);
		}

        #endregion

        public static ZCashBlockHeader Parse(string hex)
        {
            return new ZCashBlockHeader(Encoders.Hex.DecodeData(hex));
        }

        internal void SetNull()
        {
            nVersion = CURRENT_VERSION;
            hashPrevBlock = 0;
            hashMerkleRoot = 0;
            hashReserved = 0;
            nTime = 0;
            nBits = 0;
            nNonce = 0;
	        nSolution = new byte[1344];
		}
    }
}
