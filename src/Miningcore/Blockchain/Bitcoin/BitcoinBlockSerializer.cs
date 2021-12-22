using JetBrains.Annotations;
using NBitcoin;

namespace Miningcore.Blockchain.Bitcoin;

[UsedImplicitly]
public class BitcoinBlockSerializer : IBitcoinBlockSerializer
{
    public byte[] SerializeBlock(BitcoinJob job, bool isPoS, byte[] header, byte[] coinbase, byte[] rawTransactionBuffer)
    {
        var transactionCount = (uint) job.BlockTemplate.Transactions.Length + 1; // +1 for prepended coinbase tx

        using(var stream = new MemoryStream())
        {
            var bs = new BitcoinStream(stream, true);

            bs.ReadWrite(ref header);
            bs.ReadWriteAsVarInt(ref transactionCount);

            bs.ReadWrite(ref coinbase);
            bs.ReadWrite(ref rawTransactionBuffer);

            // POS coins require a zero byte appended to block which the daemon replaces with the signature
            if(isPoS)
                bs.ReadWrite((byte) 0);

            return stream.ToArray();
        }
    }
}