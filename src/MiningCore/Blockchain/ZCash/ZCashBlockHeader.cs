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


        private uint256[] hashes;
        private uint256 hashMerkleRoot;
        private uint256 hashPrevBlock;
        private uint nBits;
        private uint nNonce;
        private uint nTime;
        private int nVersion;

        internal const int Size = 80;

        // header
        private const int CURRENT_VERSION = 3;

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

        #region IBitcoinSerializable Members

        public void ReadWrite(BitcoinStream stream)
        {
            stream.ReadWrite(ref nVersion);
            stream.ReadWrite(ref hashPrevBlock);
            stream.ReadWrite(ref hashMerkleRoot);
            stream.ReadWrite(ref nTime);
            stream.ReadWrite(ref nBits);
            stream.ReadWrite(ref nNonce);
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
            nTime = 0;
            nBits = 0;
            nNonce = 0;
        }

        /// <summary>
        ///     If called, GetHash becomes cached, only use if you believe the instance will not be modified after calculation.
        ///     Calling it a second type invalidate the cache.
        /// </summary>
        public void CacheHashes()
        {
            hashes = new uint256[1];
        }
    }
}
