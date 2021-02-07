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
using Miningcore.Persistence;

namespace Miningcore.Extensions
{
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

                    catch(Exception ex)
                    {
                        tx.Rollback();
                        throw new Exception($"ERROR: Database transaction failed {ex.Message}");
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
}
