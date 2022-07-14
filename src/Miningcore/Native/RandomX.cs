using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using NLog;

// ReSharper disable UnusedMember.Global
// ReSharper disable InconsistentNaming

namespace Miningcore.Native;

public static unsafe class RandomX
{
    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
    internal static IMessageBus messageBus;

    #region VM managment

    internal static readonly Dictionary<string, Dictionary<string, Tuple<GenContext, BlockingCollection<RxVm>>>> realms = new();
    private static readonly byte[] empty = new byte[32];

    #endregion // VM managment

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
    private static extern IntPtr alloc_cache(randomx_flags flags);

    [DllImport("librandomx", EntryPoint = "randomx_init_cache", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr init_cache(IntPtr cache, IntPtr key, int keysize);

    [DllImport("librandomx", EntryPoint = "randomx_release_cache", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr release_cache(IntPtr cache);

    [DllImport("librandomx", EntryPoint = "randomx_alloc_dataset", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr alloc_dataset(randomx_flags flags);

    [DllImport("librandomx", EntryPoint = "randomx_dataset_item_count", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong dataset_item_count();

    [DllImport("librandomx", EntryPoint = "randomx_init_dataset", CallingConvention = CallingConvention.Cdecl)]
    private static extern void init_dataset(IntPtr dataset, IntPtr cache, ulong startItem, ulong itemCount);

    [DllImport("librandomx", EntryPoint = "randomx_get_dataset_memory", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr get_dataset_memory(IntPtr dataset);

    [DllImport("librandomx", EntryPoint = "randomx_release_dataset", CallingConvention = CallingConvention.Cdecl)]
    private static extern void release_dataset(IntPtr dataset);

    [DllImport("librandomx", EntryPoint = "randomx_create_vm", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr create_vm(randomx_flags flags, IntPtr cache, IntPtr dataset);

    [DllImport("librandomx", EntryPoint = "randomx_vm_set_cache", CallingConvention = CallingConvention.Cdecl)]
    private static extern void vm_set_cache(IntPtr machine, IntPtr cache);

    [DllImport("librandomx", EntryPoint = "randomx_vm_set_dataset", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr vm_set_dataset(IntPtr machine, IntPtr dataset);

    [DllImport("librandomx", EntryPoint = "randomx_destroy_vm", CallingConvention = CallingConvention.Cdecl)]
    private static extern void destroy_vm(IntPtr machine);

    [DllImport("librandomx", EntryPoint = "randomx_calculate_hash", CallingConvention = CallingConvention.Cdecl)]
    private static extern void calculate_hash(IntPtr machine, byte* input, int inputSize, byte* output);

    public class GenContext
    {
        public DateTime LastAccess { get; set; } = DateTime.Now;
        public int VmCount { get; init; }
    }

    public class RxDataSet : IDisposable
    {
        private IntPtr dataset = IntPtr.Zero;

        public void Dispose()
        {
            if(dataset != IntPtr.Zero)
            {
                release_dataset(dataset);
                dataset = IntPtr.Zero;
            }
        }

        public IntPtr Init(randomx_flags flags, IntPtr cache)
        {
            dataset = alloc_dataset(flags);

            var itemCount = dataset_item_count();
            init_dataset(dataset, cache, 0, itemCount);

            return dataset;
        }
    }

    public class RxVm : IDisposable
    {
        private IntPtr cache = IntPtr.Zero;
        private IntPtr vm = IntPtr.Zero;
        private RxDataSet ds;

        public void Dispose()
        {
            if(vm != IntPtr.Zero)
            {
                destroy_vm(vm);
                vm = IntPtr.Zero;
            }

            ds?.Dispose();

            if(cache != IntPtr.Zero)
            {
                release_cache(cache);
                cache = IntPtr.Zero;
            }
        }

        public void Init(ReadOnlySpan<byte> key, randomx_flags flags)
        {
            var ds_ptr = IntPtr.Zero;

            // alloc cache
            cache = alloc_cache(flags);

            // init cache
            fixed(byte* key_ptr = key)
            {
                init_cache(cache, (IntPtr) key_ptr, key.Length);
            }

            // Enable fast-mode? (requires 2GB+ memory per VM)
            if((flags & randomx_flags.RANDOMX_FLAG_FULL_MEM) != 0)
            {
                ds = new RxDataSet();
                ds_ptr = ds.Init(flags, cache);

                // cache is no longer needed in fast-mode
                release_cache(cache);
                cache = IntPtr.Zero;
            }

            vm = create_vm(flags, cache, ds_ptr);
        }

        public void CalculateHash(ReadOnlySpan<byte> data, Span<byte> result)
        {
            fixed (byte* input = data)
            {
                fixed (byte* output = result)
                {
                    calculate_hash(vm, input, data.Length, output);
                }
            }
        }
    }

    public static void WithLock(Action action)
    {
        lock(realms)
        {
            action();
        }
    }

    public static void CreateSeed(string realm, string seedHex,
        randomx_flags? flagsOverride = null, randomx_flags? flagsAdd = null, int vmCount = 1)
    {
        lock(realms)
        {
            if(!realms.TryGetValue(realm, out var seeds))
            {
                seeds = new Dictionary<string, Tuple<GenContext, BlockingCollection<RxVm>>>();

                realms[realm] = seeds;
            }

            if(!seeds.TryGetValue(seedHex, out var seed))
            {
                var flags = flagsOverride ?? randomx_get_flags();

                if(flagsAdd.HasValue)
                    flags |= flagsAdd.Value;

                if (vmCount == -1)
                    vmCount = Environment.ProcessorCount;

                seed = CreateSeed(realm, seedHex, flags, vmCount);

                seeds[seedHex] = seed;
            }
        }
    }

    private static Tuple<GenContext, BlockingCollection<RxVm>> CreateSeed(string realm, string seedHex, randomx_flags flags, int vmCount)
    {
        var vms = new BlockingCollection<RxVm>();

        var seed = new Tuple<GenContext, BlockingCollection<RxVm>>(new GenContext
        {
            VmCount = vmCount
        }, vms);

        void createVm(int index)
        {
            var start = DateTime.Now;
            logger.Info(() => $"Creating VM {realm}@{index + 1} [{flags}], hash {seedHex} ...");

            var vm = new RxVm();
            vm.Init(seedHex.HexToByteArray(), flags);

            vms.Add(vm);

            logger.Info(() => $"Created VM {realm}@{index + 1} in {DateTime.Now - start}");
        };

        Parallel.For(0, vmCount, createVm);

        return seed;
    }

    public static void DeleteSeed(string realm, string seedHex)
    {
        Tuple<GenContext, BlockingCollection<RxVm>> seed;

        lock(realms)
        {
            if(!realms.TryGetValue(realm, out var seeds))
                return;

            if(!seeds.Remove(seedHex, out seed))
                return;
        }

        // dispose all VMs
        var (ctx, col) = seed;
        var remaining = ctx.VmCount;

        while (remaining > 0)
        {
            var vm = col.Take();

            logger.Info($"Disposing VM {ctx.VmCount - remaining} for realm {realm} and key {seedHex}");
            vm.Dispose();

            remaining--;
        }
    }

    public static Tuple<GenContext, BlockingCollection<RxVm>> GetSeed(string realm, string seedHex)
    {
        lock(realms)
        {
            if(!realms.TryGetValue(realm, out var seeds))
                return null;

            if(!seeds.TryGetValue(seedHex, out var seed))
                return null;

            return seed;
        }
    }

    public static void CalculateHash(string realm, string seedHex, ReadOnlySpan<byte> data, Span<byte> result)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32);

        var sw = Stopwatch.StartNew();
        var success = false;

        var (ctx, seedVms) = GetSeed(realm, seedHex);

        if(ctx != null)
        {
            RxVm vm = null;

            try
            {
                // lease a VM
                vm = seedVms.Take();

                vm.CalculateHash(data, result);

                ctx.LastAccess = DateTime.Now;
                success = true;

                messageBus?.SendTelemetry("RandomX", TelemetryCategory.Hash, sw.Elapsed, true);
            }

            catch(Exception ex)
            {
                logger.Error(() => ex.Message);
            }

            finally
            {
                // return it
                if(vm != null)
                    seedVms.Add(vm);
            }
        }

        if(!success)
        {
            // clear result on failure
            empty.CopyTo(result);
        }
    }
}
