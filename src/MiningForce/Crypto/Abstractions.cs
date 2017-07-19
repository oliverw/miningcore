namespace MiningForce.Crypto
{
	public interface IHashAlgorithm
	{
		byte[] Transform(byte[] data, object args);
	}
}
