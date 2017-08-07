using Newtonsoft.Json;

namespace MiningCore.Blockchain.Monero.DaemonRequests
{
    public class GetBlockHeaderByHeightRequest
	{
		public ulong Height { get; set; }
	}
}
