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
using System.Runtime.CompilerServices;
using MiningCore.Configuration;
using MiningCore.Mining;
using NLog;

namespace MiningCore.Util
{
    public static class LogUtil
    {
        public static ILogger GetPoolScopedLogger(Type type, PoolConfig poolConfig)
        {
            return LogManager.GetLogger(poolConfig.Id);
        }

        public static void ThrowLogPoolStartupException(this ILogger logger, string msg, string category = null)
        {
            var output = !string.IsNullOrEmpty(category) ? $"[{category}] {msg}" : msg;
            logger.Error(output);

            throw new PoolStartupAbortException(msg);
        }

        public static void ThrowLogPoolStartupException(this ILogger logger, Exception ex, string msg,
            string category = null)
        {
            var output = !string.IsNullOrEmpty(category) ? $"[{category}] {msg}" : msg;
            logger.Error(ex, output);

            throw new PoolStartupAbortException(msg);
        }
    }
}
