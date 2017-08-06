using Newtonsoft.Json;

namespace MiningForce.Blockchain.Monero.DaemonRequests
{
    public class GetBlockHeaderByHeightRequest
	{
		public ulong Height { get; set; }
	}
}
