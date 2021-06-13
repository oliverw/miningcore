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
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Miningcore.Contracts;
using Miningcore.Extensions;
using MoreLinq;
using NLog;

// ReSharper disable InconsistentNaming

namespace Miningcore.Native
{
    public static unsafe class LibRandomX
    {
        #region Context managment

        private static readonly Dictionary<string, RxVm> vms = new();
        private static readonly Thread gcThread;

        static LibRandomX()
        {
            // GC
            gcThread = new Thread(() =>
            {
                while(true)
                {
                    Thread.Sleep(TimeSpan.FromMinutes(1));

                    lock(vms)
                    {
                        var list = new List<KeyValuePair<string, RxVm>>();

                        foreach(var pair in vms)
                        {
                            if(DateTime.Now - pair.Value.LastAccess > TimeSpan.FromMinutes(5))
                                list.Add(pair);
                        }

                        foreach(var pair in list.OrderBy(x=> DateTime.Now - x.Value.LastAccess, OrderByDirection.Descending))
                        {
                            // don't dispose remaining VM
                            if(vms.Count <= 1)
                                break;

                            pair.Value.Dispose();
                            vms.Remove(pair.Key);

                            logger.Info(()=> $"Disposing VM for seed hash {pair.Key}");
                        }
                    }
                }
            });

            gcThread.Start();
        }

        #endregion // Context managment

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        [Flags]
        public enum randomx_flags
        {
            RANDOMX_FLAG_DEFAULT = 0,
            RANDOMX_FLAG_LARGE_PAGES = 1,
            RANDOMX_FLAG_HARD_AES = 2,
            RANDOMX_FLAG_FULL_MEM = 4,
            RANDOMX_FLAG_JIT = 8,
            RANDOMX_FLAG_SECURE = 16,
            RANDOMX_FLAG_ARGON2_SSSE3 = 32,
            RANDOMX_FLAG_ARGON2_AVX2 = 64,
            RANDOMX_FLAG_ARGON2 = 96
        };

        [DllImport("librandomx", EntryPoint = "randomx_get_flags", CallingConvention = CallingConvention.Cdecl)]
        private static extern randomx_flags randomx_get_flags();

        [DllImport("librandomx", EntryPoint = "randomx_alloc_cache", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr randomx_alloc_cache(randomx_flags flags);

        [DllImport("librandomx", EntryPoint = "randomx_init_cache", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr randomx_init_cache(IntPtr cache, IntPtr key, int keysize);

        [DllImport("librandomx", EntryPoint = "randomx_release_cache", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr randomx_release_cache(IntPtr cache);

        [DllImport("librandomx", EntryPoint = "randomx_alloc_dataset", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr randomx_alloc_dataset(randomx_flags flags);

        [DllImport("librandomx", EntryPoint = "randomx_dataset_item_count", CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong randomx_dataset_item_count();

        [DllImport("librandomx", EntryPoint = "randomx_init_dataset", CallingConvention = CallingConvention.Cdecl)]
        private static extern void randomx_init_dataset(IntPtr dataset, IntPtr cache, ulong startItem, ulong itemCount);

        [DllImport("librandomx", EntryPoint = "randomx_get_dataset_memory", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr randomx_get_dataset_memory(IntPtr dataset);

        [DllImport("librandomx", EntryPoint = "randomx_release_dataset", CallingConvention = CallingConvention.Cdecl)]
        private static extern void randomx_release_dataset(IntPtr dataset);

        [DllImport("librandomx", EntryPoint = "randomx_create_vm", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr randomx_create_vm(randomx_flags flags, IntPtr cache, IntPtr dataset);

        [DllImport("librandomx", EntryPoint = "randomx_vm_set_cache", CallingConvention = CallingConvention.Cdecl)]
        private static extern void randomx_vm_set_cache(IntPtr machine, IntPtr cache);

        [DllImport("librandomx", EntryPoint = "randomx_vm_set_dataset", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr randomx_vm_set_dataset(IntPtr machine, IntPtr dataset);

        [DllImport("librandomx", EntryPoint = "randomx_destroy_vm", CallingConvention = CallingConvention.Cdecl)]
        private static extern void randomx_destroy_vm(IntPtr machine);

        [DllImport("librandomx", EntryPoint = "randomx_calculate_hash", CallingConvention = CallingConvention.Cdecl)]
        private static extern void randomx_calculate_hash(IntPtr machine, byte* input, int inputSize, byte* output);

        private class RxDataSet : IDisposable
        {
            private IntPtr dataset = IntPtr.Zero;

            public void Dispose()
            {
                if(dataset != IntPtr.Zero)
                {
                    randomx_release_dataset(dataset);
                    dataset = IntPtr.Zero;
                }
            }

            public IntPtr Init(ReadOnlySpan<byte> key, randomx_flags flags, IntPtr cache)
            {
                dataset = randomx_alloc_dataset(flags);

                var itemCount = randomx_dataset_item_count();
                randomx_init_dataset(dataset, cache, 0, itemCount);

                return dataset;
            }
        }

        private class RxVm : IDisposable
        {
            private IntPtr vm = IntPtr.Zero;
            private IntPtr cache = IntPtr.Zero;
            private RxDataSet ds;
            private ulong flags;
            private DateTime lastAccess;

            public IntPtr Handle => vm;
            public DateTime LastAccess => lastAccess;
            public ulong Flags => flags;

            public void Dispose()
            {
                if(vm != IntPtr.Zero)
                {
                    randomx_destroy_vm(vm);
                    vm = IntPtr.Zero;
                }

                ds?.Dispose();

                if(cache != IntPtr.Zero)
                {
                    randomx_release_cache(cache);
                    cache = IntPtr.Zero;
                }
            }

            public void Init(ReadOnlySpan<byte> key)
            {
                lastAccess = DateTime.Now;

                var flags = randomx_get_flags();
                //flags = randomx_flags.RANDOMX_FLAG_DEFAULT;
                this.flags = (ulong) flags;

                cache = randomx_alloc_cache(flags);

                fixed(byte* key_ptr = key)
                {
                    randomx_init_cache(cache, (IntPtr) key_ptr, key.Length);
                }

                ds = new RxDataSet();
                var ds_ptr = ds.Init(key, flags, cache);

                vm = randomx_create_vm(flags, cache, ds_ptr);
            }

            public void CalculateHash(ReadOnlySpan<byte> data, Span<byte> result)
            {
                Contract.Requires<ArgumentException>(result.Length >= 32, $"{nameof(result)} must be greater or equal 32 bytes");

                fixed (byte* input = data)
                {
                    fixed (byte* output = result)
                    {
                        randomx_calculate_hash(vm, input, data.Length, output);

                        lastAccess = DateTime.Now;
                    }
                }
            }
        }

        public static void CalculateHash(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data, Span<byte> result)
        {
            Contract.Requires<ArgumentException>(result.Length >= 32, $"{nameof(result)} must be greater or equal 32 bytes");

            lock(vms)
            {
                var keyString = key.ToHexString();

                if(!vms.TryGetValue(keyString, out var vm))
                {
                    var start = DateTime.Now;
                    logger.Info(()=> $"Creating VM for seed hash {keyString}. This may take a while ...");

                    vm = new RxVm();
                    vm.Init(key);

                    logger.Info(()=> $"VM created in {DateTime.Now - start} (Flags = 0x{vm.Flags:X})");

                    vms[keyString] = vm;
                }

                fixed (byte* input = data)
                {
                    fixed (byte* output = result)
                    {
                        randomx_calculate_hash(vm.Handle, input, data.Length, output);
                    }
                }
            }
        }
    }
}
