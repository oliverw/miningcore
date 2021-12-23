using Miningcore.Extensions;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace Miningcore.Blockchain.Equihash;

public class EquihashBlockHeader : IBitcoinSerializable
{
    public EquihashBlockHeader(string hex)
        : this(Encoders.Hex.DecodeData(hex))
    {
    }

    public EquihashBlockHeader(byte[] bytes)
    {
        ReadWrite(new BitcoinStream(bytes));
    }

    public EquihashBlockHeader()
    {
        SetNull();
    }

    private uint256 hashMerkleRoot;
    private uint256 hashPrevBlock;
    private byte[] hashReserved = new byte[32];
    private uint nBits;
    private string nNonce;
    private uint nTime;
    private int nVersion;

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

    public string Nonce
    {
        get => nNonce;
        set => nNonce = value;
    }

    public uint256 HashMerkleRoot
    {
        get => hashMerkleRoot;
        set => hashMerkleRoot = value;
    }

    public byte[] HashReserved
    {
        get => hashReserved;
        set => hashReserved = value;
    }

    public bool IsNull => nBits == 0;

    public uint NTime
    {
        get => nTime;
        set => nTime = value;
    }

    public DateTimeOffset BlockTime
    {
        get => Utils.UnixTimeToDateTime(nTime);
        set => nTime = Utils.DateTimeToUnixTime(value);
    }

    #region IBitcoinSerializable Members

    public void ReadWrite(BitcoinStream stream)
    {
        var nonceBytes = nNonce.HexToByteArray();

        stream.ReadWrite(ref nVersion);
        stream.ReadWrite(ref hashPrevBlock);
        stream.ReadWrite(ref hashMerkleRoot);
        stream.ReadWrite(ref hashReserved);
        stream.ReadWrite(ref nTime);
        stream.ReadWrite(ref nBits);
        stream.ReadWrite(ref nonceBytes);
    }

    #endregion

    public static EquihashBlockHeader Parse(string hex)
    {
        return new(Encoders.Hex.DecodeData(hex));
    }

    internal void SetNull()
    {
        nVersion = CURRENT_VERSION;
        hashPrevBlock = 0;
        hashMerkleRoot = 0;
        hashReserved = new byte[32];
        nTime = 0;
        nBits = 0;
        nNonce = string.Empty;
    }
}
