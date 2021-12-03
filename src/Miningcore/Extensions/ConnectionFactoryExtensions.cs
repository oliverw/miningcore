using System.Data;
using Miningcore.Persistence;

namespace Miningcore.Extensions;

public static class ConnectionFactoryExtensions
{
    /// <summary>
    /// Run the specified action providing it with a fresh connection returing its result.
    /// </summary>
    /// <returns>The result returned by the action</returns>
    public static async Task Run(this IConnectionFactory factory,
        Func<IDbConnection, Task> action)
    {
        using(var con = await factory.OpenConnectionAsync())
        {
            await action(con);
        }
    }

    /// <summary>
    /// Run the specified action providing it with a fresh connection returing its result.
    /// </summary>
    /// <returns>The result returned by the action</returns>
    public static async Task<T> Run<T>(this IConnectionFactory factory,
        Func<IDbConnection, Task<T>> action)
    {
        using(var con = await factory.OpenConnectionAsync())
        {
            return await action(con);
        }
    }

    /// <summary>
    /// Run the specified action inside a transaction. If the action throws an exception,
    /// the transaction is rolled back. Otherwise it is commited.
    /// </summary>
    public static async Task RunTx(this IConnectionFactory factory,
        Func<IDbConnection, IDbTransaction, Task> action,
        bool autoCommit = true, IsolationLevel isolation = IsolationLevel.ReadCommitted)
    {
        using(var con = await factory.OpenConnectionAsync())
        {
            using(var tx = con.BeginTransaction(isolation))
            {
                try
                {
                    await action(con, tx);

                    if(autoCommit)
                        tx.Commit();
                }

                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }
    }

    /// <summary>
    /// Run the specified action inside a transaction. If the action throws an exception,
    /// the transaction is rolled back. Otherwise it is commited.
    /// </summary>
    /// <returns>The result returned by the action</returns>
    public static async Task<T> RunTx<T>(this IConnectionFactory factory,
        Func<IDbConnection, IDbTransaction, Task<T>> func,
        bool autoCommit = true, IsolationLevel isolation = IsolationLevel.ReadCommitted)
    {
        using(var con = await factory.OpenConnectionAsync())
        {
            using(var tx = con.BeginTransaction(isolation))
            {
                try
                {
                    var result = await func(con, tx);

                    if(autoCommit)
                        tx.Commit();

                    return result;
                }

                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }
    }
}
