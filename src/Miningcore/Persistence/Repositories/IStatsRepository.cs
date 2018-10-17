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
using System.Threading.Tasks;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Model.Projections;
using MinerStats = Miningcore.Persistence.Model.Projections.MinerStats;

namespace Miningcore.Persistence.Repositories
{
    public interface IStatsRepository
    {
        Task InsertPoolStatsAsync(IDbConnection con, IDbTransaction tx, PoolStats stats);
        Task InsertMinerWorkerPerformanceStatsAsync(IDbConnection con, IDbTransaction tx, MinerWorkerPerformanceStats stats);
        Task<PoolStats> GetLastPoolStatsAsync(IDbConnection con, string poolId);
        Task<decimal> GetTotalPoolPaymentsAsync(IDbConnection con, string poolId);
        Task<PoolStats[]> GetPoolPerformanceBetweenHourlyAsync(IDbConnection con, string poolId, DateTime start, DateTime end);
        Task<MinerStats> GetMinerStatsAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner);
        Task<MinerWorkerPerformanceStats[]> PagePoolMinersByHashrateAsync(IDbConnection con, string poolId, DateTime from, int page, int pageSize);
        Task<WorkerPerformanceStatsContainer[]> GetMinerPerformanceBetweenHourlyAsync(IDbConnection con, string poolId, string miner, DateTime start, DateTime end);
        Task<WorkerPerformanceStatsContainer[]> GetMinerPerformanceBetweenDailyAsync(IDbConnection con, string poolId, string miner, DateTime start, DateTime end);
        Task<int> DeletePoolStatsBeforeAsync(IDbConnection con, DateTime date);
        Task<int> DeleteMinerStatsBeforeAsync(IDbConnection con, DateTime date);
    }
}
