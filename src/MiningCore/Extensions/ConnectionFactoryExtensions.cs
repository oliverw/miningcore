using System;
using System.Data;
using System.Threading.Tasks;
using MiningCore.Persistence;

namespace MiningCore.Extensions
{
    public static class ConnectionFactoryExtensions
    {
        /// <summary>
        /// Run the specified action providing it with a fresh connection. 
        /// </summary>
        public static void Run(this IConnectionFactory factory, Action<IDbConnection> action)
        {
            using (var con = factory.OpenConnection())
            {
                action(con);
            }
        }

        /// <summary>
        /// Run the specified action providing it with a fresh connection returing its result.
        /// </summary>
        /// <returns>The result returned by the action</returns>
        public static T Run<T>(this IConnectionFactory factory, Func<IDbConnection, T> action)
        {
            using (var con = factory.OpenConnection())
            {
                return action(con);
            }
        }

        /// <summary>
        /// Run the specified action providing it with a fresh connection returing its result.
        /// </summary>
        /// <returns>The result returned by the action</returns>
        public static async Task<T> RunAsync<T>(this IConnectionFactory factory,
            Func<IDbConnection, Task<T>> action)
        {
            using (var con = factory.OpenConnection())
            {
                return await action(con);
            }
        }

        /// <summary>
        /// Run the specified action inside a transaction. If the action throws an exception,
        /// the transaction is rolled back. Otherwise it is commited.
        /// </summary>
        public static void RunTx(this IConnectionFactory factory,
            Action<IDbConnection, IDbTransaction> action,
            bool autoCommit = true, IsolationLevel isolation = IsolationLevel.ReadCommitted)
        {
            using (var con = factory.OpenConnection())
            {
                using (var tx = con.BeginTransaction(isolation))
                {
                    try
                    {
                        action(con, tx);

                        if (autoCommit)
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
        public static T RunTx<T>(this IConnectionFactory factory,
            Func<IDbConnection, IDbTransaction, T> action,
            bool autoCommit = true, IsolationLevel isolation = IsolationLevel.ReadCommitted)
        {
            using (var con = factory.OpenConnection())
            {
                using (var tx = con.BeginTransaction(isolation))
                {
                    try
                    {
                        var result = action(con, tx);

                        if (autoCommit)
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

        /// <summary>
        /// Run the specified action inside a transaction. If the action throws an exception,
        /// the transaction is rolled back. Otherwise it is commited. 
        /// </summary>
        /// <returns>The result returned by the action</returns>
        public static async Task<T> RunTxAsync<T>(this IConnectionFactory factory,
            Func<IDbConnection, IDbTransaction, Task<T>> action,
            bool autoCommit = true, IsolationLevel isolation = IsolationLevel.ReadCommitted)
        {
            using (var con = factory.OpenConnection())
            {
                using (var tx = con.BeginTransaction(isolation))
                {
                    try
                    {
                        var result = await action(con, tx);

                        if (autoCommit)
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

        /// <summary>
        /// Run the specified action inside a transaction. If the action throws an exception,
        /// the transaction is rolled back. Otherwise it is commited. 
        /// </summary>
        /// <returns>The result returned by the action</returns>
        public static async Task RunTxAsync(this IConnectionFactory factory,
            Func<IDbConnection, IDbTransaction, Task> action,
            bool autoCommit = true, IsolationLevel isolation = IsolationLevel.ReadCommitted)
        {
            using (var con = factory.OpenConnection())
            {
                using (var tx = con.BeginTransaction(isolation))
                {
                    try
                    {
                        await action(con, tx);

                        if (autoCommit)
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
    }
}