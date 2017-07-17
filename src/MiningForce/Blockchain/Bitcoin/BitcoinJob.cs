using System;
using System.Collections.Generic;
using System.Text;
using MiningForce.Blockchain.Bitcoin.Commands;
using MiningForce.Crypto;
using MiningForce.Extensions;

namespace MiningForce.Blockchain.Bitcoin
{
    public class BitcoinJob
    {
        public BitcoinJob(long id, GetBlockTemplateResponse blockTemplate)
        {
            this.Id = id;
            BlockTemplate = blockTemplate;

            PreviousBlockHash = blockTemplate.PreviousBlockhash.HexToByteArray().ToHexString();
            //PreviousBlockHashReversed = blockTemplate.PreviousBlockhash.HexToByteArray().ReverseByteOrder().ToHexString();
        }

        public long Id { get; }
        public GetBlockTemplateResponse BlockTemplate { get; }
        public MerkleTree MTree { get; set; }
        public string PreviousBlockHash { get; }
        public string PreviousBlockHashReversed { get; set; }
        public string CoinbaseInitial { get; set; }
        public string CoinbaseFinal { get; set; }
        public string Version { get; set; }
        public string EncodedDifficulty { get; set; }
        public string NTime { get; set; }

        public object GetJobParams()
        {
            return new object[]
            {
                Id.ToString("x"),
                PreviousBlockHashReversed,
                CoinbaseInitial,
                CoinbaseFinal,
                MTree.Branches,
                Version,
                EncodedDifficulty,
                NTime,
            };
        }
    }
}
