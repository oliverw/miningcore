namespace Miningcore.Blockchain.Bitcoin;

public interface IBitcoinBlockSerializer
{
    byte[] SerializeBlock(BitcoinJob job, bool isPoS, byte[] header, byte[] coinbase, byte[] rawTransactionBuffer);
}
