using System;
using System.Threading.Tasks;
using MiningForce.Stratum;

namespace MiningForce.Blockchain
{
    public class NetworkStats
    {
        public string Network { get; set; }
        public double HashRate { get; set; }
        public DateTime? LastBlockTime { get; set; }
        public double Difficulty { get; set; }
        public int BlockHeight { get; set; }
        public int ConnectedPeers { get; set; }
        public string RewardType { get; set; }
    }

	public interface IShare
	{
		/// <summary>
		/// Who mined it
		/// </summary>
		string Worker { get; }

		/// <summary>
		/// From where was it submitted
		/// </summary>
		string IpAddress { get; }

		/// <summary>
		/// When was it submitted
		/// </summary>
		DateTime Submitted { get; }
	}

    public interface IBlockchainJobManager
    {
        Task StartAsync(StratumServer stratum);
        Task<bool> ValidateAddressAsync(string address);

        Task<object[]> HandleWorkerSubscribeAsync(StratumClient worker);
        Task<bool> HandleWorkerAuthenticateAsync(StratumClient worker, string workername, string password);
        Task<IShare> HandleWorkerSubmitShareAsync(StratumClient worker, object submission, double stratumDifficulty);

        IObservable<object> Jobs { get; }
        NetworkStats NetworkStats { get; }
    }
}
