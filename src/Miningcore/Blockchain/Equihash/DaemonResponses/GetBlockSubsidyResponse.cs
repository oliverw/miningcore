using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Miningcore.Blockchain.Equihash.DaemonResponses
{
    public class ZCashBlockSubsidy
    {
        public decimal Miner { get; set; }
        public decimal? Founders { get; set; }
        public decimal? Community { get; set; }
        public List<fundingStreams> FundingStreams { get; set; }
    }

    public class fundingStreams
    {
        public string Recipient { get; set; }
        public string Specification { get; set; }
        public decimal Value { get; set; }
        public decimal ValueZat { get; set; }
        public string Address { get; set; }
    }
}
