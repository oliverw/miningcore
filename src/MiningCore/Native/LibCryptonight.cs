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
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using MiningCore.Contracts;
using NLog;

namespace MiningCore.Native
{
    public static unsafe class LibCryptonight
    {
        #region Hashing context managment

        internal class CryptonightContextStore
        {
            internal CryptonightContextStore(Func<IntPtr> allocator, string logId)
            {
                this.logId = logId;

                // allocate context per CPU
                for (var i = 0; i < contexts.BoundedCapacity; i++)
                    contexts.Add(allocator());
            }

            private readonly string logId;

            // this holds a finite number of contexts for the cryptonight hashing functions
            // if no context is currently available because all are in use, the thread waits
            private readonly BlockingCollection<IntPtr> contexts = new BlockingCollection<IntPtr>(Environment.ProcessorCount);

            internal IntPtr Lease()
            {
                logger.Debug(()=> $"Leasing {logId} context {contexts.Count}");

                return contexts.Take();
            }

            internal void Return(IntPtr ctx)
            {
                contexts.Add(ctx);

                logger.Debug(() => $"Returned {logId} context {contexts.Count}");
            }
        }

        private static readonly CryptonightContextStore contextNormal = new CryptonightContextStore(cryptonight_alloc_context, "cn");
        private static readonly CryptonightContextStore contextLite = new CryptonightContextStore(cryptonight_alloc_lite_context, "cn-lite");
        private static readonly CryptonightContextStore contextHeavy = new CryptonightContextStore(cryptonight_alloc_heavy_context, "cn-heavy");

        #endregion // Hashing context managment

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        [DllImport("libcryptonight", EntryPoint = "cryptonight_alloc_context_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr cryptonight_alloc_context();

        [DllImport("libcryptonight", EntryPoint = "cryptonight_alloc_lite_context_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr cryptonight_alloc_lite_context();

        [DllImport("libcryptonight", EntryPoint = "cryptonight_alloc_heavy_context_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr cryptonight_alloc_heavy_context();

        [DllImport("libcryptonight", EntryPoint = "cryptonight_free_context_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern void cryptonight_free_context(IntPtr ptr);

        [DllImport("libcryptonight", EntryPoint = "cryptonight_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern int cryptonight(IntPtr ctx, byte* input, byte* output, uint inputLength, int variant);

        [DllImport("libcryptonight", EntryPoint = "cryptonight_light_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern int cryptonight_light(IntPtr ctx, byte* input, byte* output, uint inputLength, int variant);

        [DllImport("libcryptonight", EntryPoint = "cryptonight_heavy_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern int cryptonight_heavy(IntPtr ctx, byte* input, byte* output, uint inputLength, int variant);

        /// <summary>
        /// Cryptonight Hash (Monero, Monero v7, v8 etc.)
        /// </summary>
        /// <param name="variant">Algorithm variant</param>
        public static void Cryptonight(ReadOnlySpan<byte> data, Span<byte> result, int variant)
        {
            Contract.Requires<ArgumentException>(result.Length >= 32, $"{nameof(result)} must be greater or equal 32 bytes");

            var ctx = contextNormal.Lease();

            try
            {
                fixed (byte* input = data)
                {
                    fixed (byte* output = result)
                    {
                        cryptonight(ctx, input, output, (uint)data.Length, variant);
                    }
                }
            }

            finally
            {
                contextNormal.Return(ctx);
            }
        }

        /// <summary>
        /// Cryptonight Lite Hash (AEON etc.)
        /// </summary>
        /// <param name="variant">Algorithm variant</param>
        public static void CryptonightLight(ReadOnlySpan<byte> data, Span<byte> result, int variant)
        {
            Contract.Requires<ArgumentException>(result.Length >= 32, $"{nameof(result)} must be greater or equal 32 bytes");

            var ctx = contextLite.Lease();

            try
            {
            fixed (byte* input = data)
            {
                fixed (byte* output = result)
                {
                    cryptonight_light(ctx, input, output, (uint)data.Length, variant);
                }
            }
            }

            finally
            {
                contextLite.Return(ctx);
            }
        }

        /// <summary>
        /// Cryptonight Heavy Hash (TUBE etc.)
        /// </summary>
        /// <param name="variant">Algorithm variant</param>
        public static void CryptonightHeavy(ReadOnlySpan<byte> data, Span<byte> result, int variant)
        {
            Contract.Requires<ArgumentException>(result.Length >= 32, $"{nameof(result)} must be greater or equal 32 bytes");

            var ctx = contextHeavy.Lease();

            try
            {
                fixed (byte* input = data)
                {
                    fixed (byte* output = result)
                    {
                        cryptonight_heavy(ctx, input, output, (uint) data.Length, variant);
                    }
                }
            }

            finally
            {
                contextHeavy.Return(ctx);
            }
        }
    }
}
