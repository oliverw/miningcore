using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Model.Projections;
using Miningcore.Persistence.Repositories;

namespace Miningcore.Tests.Persistence.Postgres.Repositories
{
    public class ShareRepository : IShareRepository
    {
        public ShareRepository(IMapper mapper)
        {
            this.mapper = mapper;
        }

        private readonly IMapper mapper;

        public Task BatchInsertAsync(IDbConnection con, IDbTransaction tx, IEnumerable<Share> shares, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task<Share[]> ReadSharesBeforeCreatedAsync(IDbConnection con, string poolId, DateTime before, bool inclusive, int pageSize, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task<long> CountSharesBeforeCreatedAsync(IDbConnection con, IDbTransaction tx, string poolId, DateTime before, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task DeleteSharesBeforeCreatedAsync(IDbConnection con, IDbTransaction tx, string poolId, DateTime before, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task<long> CountSharesByMinerAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task DeleteSharesByMinerAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task<double?> GetAccumulatedShareDifficultyBetweenCreatedAsync(IDbConnection con, string poolId, DateTime start, DateTime end, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task<MinerWorkerHashes[]> GetHashAccumulationBetweenCreatedAsync(IDbConnection con, string poolId, DateTime start, DateTime end, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task<string[]> GetRecentyUsedIpAddressesAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner, CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task<KeyValuePair<string, double>[]> GetAccumulatedUserAgentShareDifficultyBetweenCreatedAsync(IDbConnection con, string poolId,
            DateTime start, DateTime end, bool byVersione, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task DeleteProcessedSharesBeforeAcceptedAsync(IDbConnection con, IDbTransaction tx, string poolId, DateTime before)
        {
            throw new NotImplementedException();
        }
        public Task<MinerWorkerHashes[]> GetHashAccumulationBetweenAcceptedAsync(IDbConnection con, string poolId, DateTime start, DateTime end, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task ProcessSharesForUserBeforeAcceptedAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner, DateTime before)
        {
            throw new NotImplementedException();
        }
        public Task<Share[]> ReadUnprocessedSharesBeforeAcceptedAsync(IDbConnection con, string poolId, DateTime before, bool inclusive, int pageSize)
        {
            throw new NotImplementedException();
        }
    }
}
