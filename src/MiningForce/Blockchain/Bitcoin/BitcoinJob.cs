using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
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
            Id = id;
            BlockTemplate = blockTemplate;

            Version = BitConverter.GetBytes(blockTemplate.Version.ToBigEndian()).ToHexString();
            
            EncodedDifficulty = blockTemplate.Bits;
            NTime = BitConverter.GetBytes(blockTemplate.CurTime.ToBigEndian()).ToHexString();

            Target = string.IsNullOrEmpty(blockTemplate.Target)
                ? EncodedDifficulty.BigIntFromBitsHex()
                : BigInteger.Parse(blockTemplate.Target, NumberStyles.HexNumber);

            Difficulty = (double) (BitcoinConstants.Diff1 / Target);

            PreviousBlockHash = blockTemplate.PreviousBlockhash
                .HexToByteArray()
                .ToHexString();

            PreviousBlockHashReversed = blockTemplate.PreviousBlockhash
                .HexToByteArray()
                .ReverseByteOrder()
                .ToHexString();

            MerkleTree = new MerkleTree(BlockTemplate.Transactions
                .Select(transaction =>
                {
                    if(!string.IsNullOrEmpty(transaction.TxId))
                        return transaction.TxId.HexToByteArray().Reverse().ToArray();

                    return transaction.Hash.HexToByteArray().Reverse().ToArray();
                }));

            //var coinbaseTx = new GenerationTransaction(ExtraNonce, _daemonClient, blockTemplate, _poolConfig)
        }

        public long Id { get; }
        public GetBlockTemplateResponse BlockTemplate { get; }
        public MerkleTree MerkleTree { get; set; }
        public string PreviousBlockHash { get; }
        public string PreviousBlockHashReversed { get; set; }
        public string CoinbaseInitial { get; set; }
        public string CoinbaseFinal { get; set; }
        public string Version { get; set; }
        public double Difficulty { get; set; }
        public string EncodedDifficulty { get; set; }
        public string NTime { get; set; }
        public BigInteger Target { get; }

        public object GetJobParams()
        {
            return new object[]
            {
                Id.ToString("x"),
                PreviousBlockHashReversed,
                CoinbaseInitial,
                CoinbaseFinal,
                MerkleTree.Branches,
                Version,
                EncodedDifficulty,
                NTime,
            };
        }
    }
}
