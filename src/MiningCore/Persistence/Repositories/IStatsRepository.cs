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
using System.Data;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Model.Projections;
using MinerStats = MiningCore.Persistence.Model.Projections.MinerStats;

namespace MiningCore.Persistence.Repositories
{
    public interface IStatsRepository
    {
        void InsertPoolStats(IDbConnection con, IDbTransaction tx, PoolStats stats);
        void InsertMinerWorkerPerformanceStats(IDbConnection con, IDbTransaction tx, MinerWorkerPerformanceStats stats);
        PoolStats GetLastPoolStats(IDbConnection con, string poolId);
        decimal GetTotalPoolPayments(IDbConnection con, string poolId);
        PoolStats[] GetPoolPerformanceBetweenHourly(IDbConnection con, string poolId, DateTime start, DateTime end);
        MinerStats GetMinerStats(IDbConnection con, IDbTransaction tx, string poolId, string miner);
        MinerWorkerPerformanceStats[] PagePoolMinersByHashrate(IDbConnection con, string poolId, DateTime from, int page, int pageSize);
        WorkerPerformanceStatsContainer[] GetMinerPerformanceBetweenHourly(IDbConnection con, string poolId, string miner, DateTime start, DateTime end);
        WorkerPerformanceStatsContainer[] GetMinerPerformanceBetweenDaily(IDbConnection con, string poolId, string miner, DateTime start, DateTime end);
    }
}
