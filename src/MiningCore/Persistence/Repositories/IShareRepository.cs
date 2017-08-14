using System;
using System.Data;
using MiningCore.Persistence.Model;

namespace MiningCore.Persistence.Repositories
{
    public interface IShareRepository
    {
        void Insert(IDbConnection con, IDbTransaction tx, Share share);
        Share[] PageSharesBefore(IDbConnection con, string poolId, DateTime before, int page, int pageSize);

        Share[] PageSharesBetween(IDbConnection con, string poolId, DateTime start, DateTime end, int page,
            int pageSize);

        long CountSharesBefore(IDbConnection con, IDbTransaction tx, string poolId, DateTime before);
        void DeleteSharesBefore(IDbConnection con, IDbTransaction tx, string poolId, DateTime before);
    }
}
