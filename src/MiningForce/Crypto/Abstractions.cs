namespace MiningForce.Crypto
{
	public interface IHashAlgorithm
	{
		byte[] Digest(byte[] data, object args);
	}
}
