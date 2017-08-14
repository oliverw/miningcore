namespace MiningCore.Crypto
{
    public interface IHashAlgorithm
    {
        byte[] Digest(byte[] data, ulong nTime);
    }
}