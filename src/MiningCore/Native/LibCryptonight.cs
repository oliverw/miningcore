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
using Miningcore.Contracts;
using NLog;

namespace Miningcore.Native
{
    public static unsafe class LibCryptonight
    {
        #region Hashing context managment

        internal class CryptonightContextStore
        {
            internal CryptonightContextStore(Func<IntPtr> allocator, int allocationSize, string logId)
            {
                this.logId = logId.ToUpper();

                // allocate context per CPU
                for(var i = 0; i < contexts.BoundedCapacity; i++)
                {
                    contexts.Add(new Lazy<IntPtr>(()=>
                    {
                        var result = allocator();

                        if(result != IntPtr.Zero)
                            GC.AddMemoryPressure(allocationSize);

                        return result;
                    }));
                }
            }

            private readonly string logId;

            // this holds a finite number of contexts for the cryptonight hashing functions
            // if no context is currently available because all are in use, the thread waits
            private readonly BlockingCollection<Lazy<IntPtr>> contexts = new BlockingCollection<Lazy<IntPtr>>(Environment.ProcessorCount);

            internal Lazy<IntPtr> Lease()
            {
                logger.Debug(()=> $"Leasing {logId} context ({contexts.Count})");

                return contexts.Take();
            }

            internal void Return(Lazy<IntPtr> ctx)
            {
                contexts.Add(ctx);

                logger.Debug(() => $"Returned {logId} context ({contexts.Count})");
            }
        }

        private static readonly CryptonightContextStore ctxs = new CryptonightContextStore(cryptonight_alloc_context, cryptonight_get_context_size(), "cn");
        private static readonly CryptonightContextStore ctxsLite = new CryptonightContextStore(cryptonight_alloc_lite_context, cryptonight_get_context_lite_size(), "cn-lite");
        private static readonly CryptonightContextStore ctxsHeavy = new CryptonightContextStore(cryptonight_alloc_heavy_context, cryptonight_get_context_heavy_size(), "cn-heavy");

        #endregion // Hashing context managment

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        [DllImport("libcryptonight", EntryPoint = "cryptonight_get_context_size_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern int cryptonight_get_context_size();

        [DllImport("libcryptonight", EntryPoint = "cryptonight_get_context_lite_size_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern int cryptonight_get_context_lite_size();

        [DllImport("libcryptonight", EntryPoint = "cryptonight_get_context_heavy_size_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern int cryptonight_get_context_heavy_size();

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

            var ctx = ctxs.Lease();

            try
            {
                fixed (byte* input = data)
                {
                    fixed (byte* output = result)
                    {
                        cryptonight(ctx.Value, input, output, (uint)data.Length, variant);
                    }
                }
            }

            finally
            {
                ctxs.Return(ctx);
            }
        }

        /// <summary>
        /// Cryptonight Lite Hash (AEON etc.)
        /// </summary>
        /// <param name="variant">Algorithm variant</param>
        public static void CryptonightLight(ReadOnlySpan<byte> data, Span<byte> result, int variant)
        {
            Contract.Requires<ArgumentException>(result.Length >= 32, $"{nameof(result)} must be greater or equal 32 bytes");

            var ctx = ctxsLite.Lease();

            try
            {
                fixed(byte* input = data)
                {
                    fixed(byte* output = result)
                    {
                        cryptonight_light(ctx.Value, input, output, (uint) data.Length, variant);
                    }
                }
            }

            finally
            {
                ctxsLite.Return(ctx);
            }
        }

        /// <summary>
        /// Cryptonight Heavy Hash (TUBE etc.)
        /// </summary>
        /// <param name="variant">Algorithm variant</param>
        public static void CryptonightHeavy(ReadOnlySpan<byte> data, Span<byte> result, int variant)
        {
            Contract.Requires<ArgumentException>(result.Length >= 32, $"{nameof(result)} must be greater or equal 32 bytes");

            var ctx = ctxsHeavy.Lease();

            try
            {
                fixed (byte* input = data)
                {
                    fixed (byte* output = result)
                    {
                        cryptonight_heavy(ctx.Value, input, output, (uint) data.Length, variant);
                    }
                }
            }

            finally
            {
                ctxsHeavy.Return(ctx);
            }
        }
    }
}
