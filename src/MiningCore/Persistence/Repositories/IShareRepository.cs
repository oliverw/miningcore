using System;
using System.Data;

namespace MiningCore.Persistence.Repositories
{
    public interface IShareRepository
    {
        void Insert(IDbConnection con, IDbTransaction tx, Model.Share share);
        Model.Share[] PageSharesBefore(IDbConnection con, string poolId, DateTime before, int page, int pageSize);

        Model.Share[] PageSharesBetween(IDbConnection con, string poolId, DateTime start, DateTime end, int page,
            int pageSize);

        long CountSharesBefore(IDbConnection con, IDbTransaction tx, string poolId, DateTime before);
        void DeleteSharesBefore(IDbConnection con, IDbTransaction tx, string poolId, DateTime before);
    }
}