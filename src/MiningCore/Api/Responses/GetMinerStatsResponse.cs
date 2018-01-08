/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using System.Collections.Generic;

namespace MiningCore.Api.Responses
{
    public class MinerPerformanceStats
    {
        public string Miner { get; set; }
        public double Hashrate { get; set; }
        public double SharesPerSecond { get; set; }
    }

    public class WorkerPerformanceStats
    {
        public double Hashrate { get; set; }
        public double SharesPerSecond { get; set; }
    }

    public class WorkerPerformanceStatsContainer
    {
        public DateTime Created { get; set; }
        public Dictionary<string, WorkerPerformanceStats> Workers { get; set; }
    }

    public class MinerStats
    {
        public ulong PendingShares { get; set; }
        public decimal PendingBalance { get; set; }
        public decimal TotalPaid { get; set; }
        public DateTime? LastPayment { get; set; }
        public string LastPaymentLink { get; set; }
        public WorkerPerformanceStatsContainer Performance { get; set; }
        public WorkerPerformanceStatsContainer[] Performance24H { get; set; }
    }
}
