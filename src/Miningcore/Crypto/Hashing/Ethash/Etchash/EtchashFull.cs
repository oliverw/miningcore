using System.Text;
using Miningcore.Blockchain.Ethereum;
using Miningcore.Contracts;
using Miningcore.Native;
using NLog;


namespace Miningcore.Crypto.Hashing.Ethash.Etchash;

[Identifier("etchash")]
public class EtchashFull : IEthashFull
{
    public void Setup(int numCaches, string dagDir, ulong hardForkBlock)
    {

        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(dagDir));

        this.numCaches = numCaches;
        this.dagDir = dagDir;
        this.hardForkBlock = hardForkBlock;
    }

    private int numCaches; // Maximum number of caches to keep before eviction (only init, don't modify)
    private readonly object cacheLock = new();
    private readonly Dictionary<ulong, Dag> caches = new();
    private Dag future;
    private string dagDir;
    private ulong hardForkBlock;
    public string AlgoName { get; } = "Etchash";

    public void Dispose()
    {
        foreach(var value in caches.Values)
            value.Dispose();
    }

    public async Task<IEthashDag> GetDagAsync(ulong block, ILogger logger, CancellationToken ct)
    {
        var dagEpochLength = block >= hardForkBlock ? EthereumClassicConstants.EpochLength : EthereumConstants.EpochLength;
        logger.Debug(() => $"Epoch length used: {dagEpochLength}");
        var epoch = block / dagEpochLength;
        Dag result;

        lock(cacheLock)
        {
            if(numCaches == 0)
                numCaches = 3;

            if(!caches.TryGetValue(epoch, out result))
            {
                // No cached DAG, evict the oldest if the cache limit was reached
                while(caches.Count >= numCaches)
                {
                    var toEvict = caches.Values.OrderBy(x => x.LastUsed).First();
                    var key = caches.First(pair => pair.Value == toEvict).Key;
                    var epochToEvict = toEvict.Epoch;

                    logger.Info(() => $"Evicting DAG for epoch {epochToEvict} in favour of epoch {epoch}");
                    toEvict.Dispose();
                    caches.Remove(key);
                }

                // If we have the new DAG pre-generated, use that, otherwise create a new one
                if(future != null && future.Epoch == epoch)
                {
                    logger.Debug(() => $"Using pre-generated DAG for epoch {epoch}");

                    result = future;
                    future = null;
                }

                else
                {
                    logger.Info(() => $"No pre-generated DAG available, creating new for epoch {epoch}");
                    result = new Dag(epoch);
                }

                caches[epoch] = result;
            }

            // If we used up the future cache, or need a refresh, regenerate
            else if(future == null || future.Epoch <= epoch)
            {
                logger.Info(() => $"Pre-generating DAG for epoch {epoch + 1}");
                future = new Dag(epoch + 1);

#pragma warning disable 4014
                future.GenerateAsync(dagDir, dagEpochLength, hardForkBlock, logger, ct);
#pragma warning restore 4014
            }

            result.LastUsed = DateTime.Now;
        }

        // get/generate current one
        await result.GenerateAsync(dagDir, dagEpochLength, hardForkBlock, logger, ct);

        return result;
    }

    public unsafe string GetDefaultDagDirectory()
    {
        var chars = new byte[512];

        fixed(byte* data = chars)
        {
            if(EtcHash.ethash_get_default_dirname(data, chars.Length))
            {
                int length;
                for(length = 0; length < chars.Length; length++)
                {
                    if(data[length] == 0)
                        break;
                }

                return Encoding.UTF8.GetString(data, length);
            }
        }

        return null;
    }
}
