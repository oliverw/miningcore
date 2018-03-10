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
using System.Linq;
using AutoMapper;
using Dapper;
using MiningCore.Extensions;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Model.Projections;
using MiningCore.Persistence.Repositories;
using MiningCore.Util;
using NLog;

namespace MiningCore.Persistence.Postgres.Repositories
{
    public class ShareRepository : IShareRepository
    {
        public ShareRepository(IMapper mapper)
        {
            this.mapper = mapper;
        }

        private readonly IMapper mapper;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        public void Insert(IDbConnection con, IDbTransaction tx, Share share)
        {
            logger.LogInvoke();

            var mapped = mapper.Map<Entities.Share>(share);

            var query = "INSERT INTO shares(poolid, blockheight, difficulty, " +
                "networkdifficulty, miner, worker, payoutinfo, useragent, ipaddress, source, created) " +
                "VALUES(@poolid, @blockheight, @difficulty, " +
                "@networkdifficulty, @miner, @worker, @payoutinfo, @useragent, @ipaddress, @source, @created)";

            con.Execute(query, mapped, tx);
        }

        public Share[] ReadSharesBeforeCreated(IDbConnection con, string poolId, DateTime before, bool inclusive, int pageSize)
        {
            logger.LogInvoke(new[] { poolId });

            var query = $"SELECT * FROM shares WHERE poolid = @poolId AND created {(inclusive ? " <= " : " < ")} @before " +
                "ORDER BY created DESC FETCH NEXT (@pageSize) ROWS ONLY";

            return con.Query<Entities.Share>(query, new { poolId, before, pageSize })
                .Select(mapper.Map<Share>)
                .ToArray();
        }

        public Share[] ReadSharesBeforeAndAfterCreated(IDbConnection con, string poolId, DateTime before, DateTime after, bool inclusive, int pageSize)
        {
            logger.LogInvoke(new[] { poolId });

            var query = $"SELECT * FROM shares WHERE poolid = @poolId AND created {(inclusive ? " <= " : " < ")} @before " +
                        $"AND created {(inclusive ? " >= " : " > ")} @after" +
                        "ORDER BY created DESC FETCH NEXT (@pageSize) ROWS ONLY";

            return con.Query<Entities.Share>(query, new { poolId, before, after, pageSize })
                .Select(mapper.Map<Share>)
                .ToArray();
        }

        public Share[] PageSharesBetweenCreated(IDbConnection con, string poolId, DateTime start, DateTime end, int page, int pageSize)
        {
            logger.LogInvoke(new[] { poolId });

            var query = "SELECT * FROM shares WHERE poolid = @poolId AND created >= @start AND created <= @end " +
                "ORDER BY created DESC OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY";

            return con.Query<Entities.Share>(query, new { poolId, start, end, offset = page * pageSize, pageSize })
                .Select(mapper.Map<Share>)
                .ToArray();
        }

        public long CountSharesBeforeCreated(IDbConnection con, IDbTransaction tx, string poolId, DateTime before)
        {
            logger.LogInvoke(new[] { poolId });

            var query = "SELECT count(*) FROM shares WHERE poolid = @poolId AND created < @before";

            return con.QuerySingle<long>(query, new { poolId, before }, tx);
        }

        public void DeleteSharesBeforeCreated(IDbConnection con, IDbTransaction tx, string poolId, DateTime before)
        {
            logger.LogInvoke(new[] { poolId });

            var query = "DELETE FROM shares WHERE poolid = @poolId AND created < @before";

            con.Execute(query, new { poolId, before }, tx);
        }

        public long CountSharesBetweenCreated(IDbConnection con, string poolId, string miner, DateTime? start, DateTime? end)
        {
            logger.LogInvoke(new[] { poolId });

            var whereClause = "poolid = @poolId AND miner = @miner";

            if (start.HasValue)
                whereClause += " AND created >= @start ";
            if (end.HasValue)
                whereClause += " AND created <= @end";

            var query = $"SELECT count(*) FROM shares WHERE {whereClause}";

            return con.QuerySingle<long>(query, new { poolId, miner, start, end });
        }

        public double? GetAccumulatedShareDifficultyBetweenCreated(IDbConnection con, string poolId, DateTime start, DateTime end)
        {
            logger.LogInvoke(new[] { poolId });

            var query = "SELECT SUM(difficulty) FROM shares WHERE poolid = @poolId AND created > @start AND created < @end";

            return con.QuerySingle<ulong?>(query, new { poolId, start, end });
        }

        public MinerWorkerHashes[] GetAccumulatedShareDifficultyTotal(IDbConnection con, string poolId)
        {
            logger.LogInvoke(new[] { (object)poolId });

            var query = "SELECT SUM(difficulty) AS sum, COUNT(difficulty) AS count, miner, worker FROM shares WHERE poolid = @poolid group by miner, worker";

            return con.Query<MinerWorkerHashes>(query, new { poolId })
                .ToArray();
        }

        public MinerWorkerHashes[] GetHashAccumulationBetweenCreated(IDbConnection con, string poolId, DateTime start, DateTime end)
        {
            logger.LogInvoke(new[] { poolId });

            var query = "SELECT SUM(difficulty), COUNT(difficulty), MIN(created) AS firstshare, MAX(created) AS lastshare, miner, worker FROM shares " +
                        "WHERE poolid = @poolId AND created >= @start AND created <= @end " +
                        "GROUP BY miner, worker";

            return con.Query<MinerWorkerHashes>(query, new { poolId, start, end })
                .ToArray();
        }
    }
}
