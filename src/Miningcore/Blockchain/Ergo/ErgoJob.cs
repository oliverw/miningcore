using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Miningcore.Blockchain.Cryptonote;
using Miningcore.Configuration;
using Miningcore.Time;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Ergo
{
    public class ErgoJob
    {
        public ErgoBlockTemplate BlockTemplate { get; private set; }
        public double Difficulty { get; private set; }
        public ulong Height => BlockTemplate.Work.Height;
        public string JobId { get; protected set; }

        protected object[] jobParams;

        public object GetJobParams(bool isNew)
        {
            jobParams[^1] = isNew;
            return jobParams;
        }

        public void Init(ErgoBlockTemplate blockTemplate, string jobId, PoolConfig poolConfig, ClusterConfig clusterConfig, IMasterClock clock, string network)
        {
            BlockTemplate = blockTemplate;
            JobId = jobId;

            // Network difficulty
            if(blockTemplate.Info.Difficulty.Type == JTokenType.Object)
            {
                if(((JObject) blockTemplate.Info.Difficulty).TryGetValue("proof-of-work", out var diffVal))
                    Difficulty = diffVal.Value<double>();
            }

            else
                Difficulty = blockTemplate.Info.Difficulty.Value<double>();

            jobParams = new object[]
            {
                JobId,
                Height,
                BlockTemplate.Work.Msg,
                string.Empty,
                string.Empty,
                BlockTemplate.Info.Parameters.BlockVersion,
                BlockTemplate.Work.B,
                string.Empty,
                false
            };
        }
    }
}
