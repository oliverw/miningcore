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
using System.Diagnostics;
using System.Threading.Tasks;
using MiningCore.Persistence;
using NLog;

namespace MiningCore.Extensions
{
    public static class ConnectionFactoryExtensions
    {
        /// <summary>
        /// Run the specified action providing it with a fresh connection.
        /// </summary>
        public static void Run(this IConnectionFactory factory, Action<IDbConnection> action)
        {
            using(var con = factory.OpenConnection())
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
            using(var con = factory.OpenConnection())
            {
                return action(con);
            }
        }

        /// <summary>
        /// Run the specified action providing it with a fresh connection.
        /// </summary>
        public static void Run(this IConnectionFactory factory, Action<IDbConnection> action, Stopwatch sw, ILogger logger)
        {
            using (var con = factory.OpenConnection())
            {
                sw.Reset();
                sw.Start();

                action(con);

                sw.Stop();
                logger.Debug(()=> $"Query took {sw.ElapsedMilliseconds} ms");
            }
        }

        /// <summary>
        /// Run the specified action providing it with a fresh connection returing its result.
        /// </summary>
        /// <returns>The result returned by the action</returns>
        public static T Run<T>(this IConnectionFactory factory, Func<IDbConnection, T> action, Stopwatch sw, ILogger logger)
        {
            using (var con = factory.OpenConnection())
            {
                sw.Reset();
                sw.Start();

                var result = action(con);

                sw.Stop();
                logger.Debug(() => $"Query took {sw.ElapsedMilliseconds} ms");
                return result;
            }
        }

        /// <summary>
        /// Run the specified action providing it with a fresh connection returing its result.
        /// </summary>
        /// <returns>The result returned by the action</returns>
        public static async Task<T> RunAsync<T>(this IConnectionFactory factory,
            Func<IDbConnection, Task<T>> action)
        {
            using(var con = factory.OpenConnection())
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
            using(var con = factory.OpenConnection())
            {
                using(var tx = con.BeginTransaction(isolation))
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
            using(var con = factory.OpenConnection())
            {
                using(var tx = con.BeginTransaction(isolation))
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
            using(var con = factory.OpenConnection())
            {
                using(var tx = con.BeginTransaction(isolation))
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
            using(var con = factory.OpenConnection())
            {
                using(var tx = con.BeginTransaction(isolation))
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
