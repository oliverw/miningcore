using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using MiningCore.Contracts;
using MiningCore.Persistence.Model;

namespace MiningCore.Persistence.Common.Repositories
{
    public class StatsRepositoryBase
    {
        private static readonly IMemoryCache cache = new MemoryCache(new MemoryCacheOptions
        {
            ExpirationScanFrequency = TimeSpan.FromSeconds(60)
        });

        private const int MaxHistorySize = 6;

        private string BuildSampleKey(string poolId, string miner)
        {
            return $"mhr_{poolId}_{miner}";
        }

        public void RecordMinerHashrateSample(IDbConnection con, IDbTransaction tx, MinerHashrateSample sample)
        {
            Contract.RequiresNonNull(sample, nameof(sample));

            var key = BuildSampleKey(sample.PoolId, sample.Miner);
            var samples = cache.Get<List<MinerHashrateSample>>(key);
            var isNew = samples == null;

            if (isNew)
            {
                samples = new List<MinerHashrateSample>(MaxHistorySize)
                {
                    sample
                };
            }

            else
            {
                while(samples.Count >= MaxHistorySize)
                    samples.Remove(samples.Last());

                samples.Insert(0, sample);
            }

            if (isNew)
            {
                cache.Set(key, samples, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(15)
                });
            }
        }

        protected MinerHashrateSample[] GetMinerHashrateSamples(string poolId, string miner)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(poolId), $"{nameof(poolId)} must not be empty");
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(miner), $"{nameof(miner)} must not be empty");

            var key = BuildSampleKey(poolId, miner);
            var samples = cache.Get<List<MinerHashrateSample>>(key);

            return samples?.ToArray();
        }
    }
}
