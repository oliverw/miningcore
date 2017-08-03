using System;
using System.Collections.Generic;

namespace MiningForce.Blockchain.Monero
{
    public class MoneroWorkerJob
	{
		public MoneroWorkerJob(string jobId, double difficulty)
		{
			Id = jobId;
			Difficulty = difficulty;
		}

		public string Id { get; }
		public uint Height { get; set; }
		public uint ExtraNonce { get; set; }
		public double Difficulty { get; set; }

		public HashSet<string> Submissions { get; } = new HashSet<string>();

		public bool RegisterSubmit(string extraNonce1, string extraNonce2, string nTime, string nonce)
		{
			var key = extraNonce1 + extraNonce2 + nTime + nonce;
			if (Submissions.Contains(key))
				return false;

			Submissions.Add(key);
			return true;
		}
	}
}
