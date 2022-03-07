using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;

// ReSharper disable UnusedMember.Global

namespace Miningcore.Native;

public static unsafe class Cryptonight
{
    [DllImport("libcryptonight", EntryPoint = "alloc_context_export", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr alloc_context();

    [DllImport("libcryptonight", EntryPoint = "free_context_export", CallingConvention = CallingConvention.Cdecl)]
    private static extern void free_context(IntPtr ctx);

    [DllImport("libcryptonight", EntryPoint = "cryptonight_export", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool cryptonight(byte* input, int inputLength, void* output, Algorithm algo, ulong height, IntPtr ctx);

    [DllImport("libcryptonight", EntryPoint = "cryptonight_lite_export", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool cryptonight_lite(byte* input, int inputLength, void* output, Algorithm algo, ulong height, IntPtr ctx);

    [DllImport("libcryptonight", EntryPoint = "cryptonight_heavy_export", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool cryptonight_heavy(byte* input, int inputLength, void* output, Algorithm algo, ulong height, IntPtr ctx);

    [DllImport("libcryptonight", EntryPoint = "cryptonight_pico_export", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool cryptonight_pico(byte* input, int inputLength, void* output, Algorithm algo, ulong height, IntPtr ctx);

    [DllImport("libcryptonight", EntryPoint = "argon_export", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool argon(byte* input, int inputLength, void* output, Algorithm algo, ulong height, IntPtr ctx);

    public enum Algorithm
    {
        INVALID         = 0,
        CN_0            = 0x63150000,   // "cn/0"             CryptoNight (original).
        CN_1            = 0x63150100,   // "cn/1"             CryptoNight variant 1 also known as Monero7 and CryptoNightV7.
        CN_2            = 0x63150200,   // "cn/2"             CryptoNight variant 2.
        CN_R            = 0x63150272,   // "cn/r"             CryptoNightR (Monero's variant 4).
        CN_FAST         = 0x63150166,   // "cn/fast"          CryptoNight variant 1 with half iterations.
        CN_HALF         = 0x63150268,   // "cn/half"          CryptoNight variant 2 with half iterations (Masari/Torque).
        CN_XAO          = 0x63150078,   // "cn/xao"           CryptoNight variant 0 (modified, Alloy only).
        CN_RTO          = 0x63150172,   // "cn/rto"           CryptoNight variant 1 (modified, Arto only).
        CN_RWZ          = 0x63150277,   // "cn/rwz"           CryptoNight variant 2 with 3/4 iterations and reversed shuffle operation (Graft).
        CN_ZLS          = 0x6315027a,   // "cn/zls"           CryptoNight variant 2 with 3/4 iterations (Zelerius).
        CN_DOUBLE       = 0x63150264,   // "cn/double"        CryptoNight variant 2 with double iterations (X-CASH).
        CN_CCX          = 0x63150063,   // "cn/ccx"           Conceal (CCX)
        CN_LITE_0       = 0x63140000,   // "cn-lite/0"        CryptoNight-Lite variant 0.
        CN_LITE_1       = 0x63140100,   // "cn-lite/1"        CryptoNight-Lite variant 1.
        CN_HEAVY_0      = 0x63160000,   // "cn-heavy/0"       CryptoNight-Heavy (4 MB).
        CN_HEAVY_TUBE   = 0x63160172,   // "cn-heavy/tube"    CryptoNight-Heavy (modified, TUBE only).
        CN_HEAVY_XHV    = 0x63160068,   // "cn-heavy/xhv"     CryptoNight-Heavy (modified, Haven Protocol only).
        CN_PICO_0       = 0x63120200,   // "cn-pico"          CryptoNight-Pico
        CN_PICO_TLO     = 0x63120274,   // "cn-pico/tlo"      CryptoNight-Pico (TLO)
        CN_UPX2         = 0x63110200,   // "cn/upx2"          Uplexa (UPX2)
        CN_GR_0         = 0x63130100,   // "cn/dark"          GhostRider
        CN_GR_1         = 0x63130101,   // "cn/dark-lite"     GhostRider
        CN_GR_2         = 0x63150102,   // "cn/fast"          GhostRider
        CN_GR_3         = 0x63140103,   // "cn/lite"          GhostRider
        CN_GR_4         = 0x63120104,   // "cn/turtle"        GhostRider
        CN_GR_5         = 0x63120105,   // "cn/turtle-lite"   GhostRider
        GHOSTRIDER_RTM  = 0x6c150000,   // "ghostrider"       GhostRider
        // RX_0            = 0x72151200,   // "rx/0"             RandomX (reference configuration).
        // RX_WOW          = 0x72141177,   // "rx/wow"           RandomWOW (Wownero).
        // RX_ARQ          = 0x72121061,   // "rx/arq"           RandomARQ (Arqma).
        // RX_GRAFT        = 0x72151267,   // "rx/graft"         RandomGRAFT (Graft).
        // RX_SFX          = 0x72151273,   // "rx/sfx"           RandomSFX (Safex Cash).
        // RX_KEVA         = 0x7214116b,   // "rx/keva"          RandomKEVA (Keva).
        AR2_CHUKWA      = 0x61130000,   // "argon2/chukwa"    Argon2id (Chukwa).
        AR2_CHUKWA_V2   = 0x61140000,   // "argon2/chukwav2"  Argon2id (Chukwa v2).
        AR2_WRKZ        = 0x61120000,   // "argon2/wrkz"      Argon2id (WRKZ)
        // ASTROBWT_DERO   = 0x41000000,   // "astrobwt"         AstroBWT (Dero)
        // KAWPOW_RVN      = 0x6b0f0000,   // "kawpow/rvn"       KawPow (RVN)

        CN_GPU          = 0x631500ff,   // "cn/gpu"           CryptoNight-GPU (Ryo).
        //RX_XLA          = 0x721211ff,   // "panthera"         Panthera (Scala2).
    }

    internal static IMessageBus messageBus;

    private static readonly HashSet<Algorithm> validCryptonightAlgos = new()
    {
        Algorithm.CN_0,
        Algorithm.CN_1,
        Algorithm.CN_FAST,
        Algorithm.CN_XAO,
        Algorithm.CN_RTO,
        Algorithm.CN_2,
        Algorithm.CN_HALF,
        Algorithm.CN_GPU,
        Algorithm.CN_R,
        Algorithm.CN_RWZ,
        Algorithm.CN_ZLS,
        Algorithm.CN_DOUBLE,
        Algorithm.CN_CCX,
        Algorithm.GHOSTRIDER_RTM,
    };

    private static readonly HashSet<Algorithm> validCryptonightLiteAlgos = new()
    {
        Algorithm.CN_LITE_0,
        Algorithm.CN_LITE_1,
    };

    private static readonly HashSet<Algorithm> validCryptonightHeavyAlgos = new()
    {
        Algorithm.CN_HEAVY_0,
        Algorithm.CN_HEAVY_XHV,
        Algorithm.CN_HEAVY_TUBE,
    };

    private static readonly HashSet<Algorithm> validCryptonightPicoAlgos = new()
    {
        Algorithm.CN_PICO_0,
        Algorithm.CN_PICO_TLO,
    };

    private static readonly HashSet<Algorithm> validArgonAlgos = new()
    {
        Algorithm.AR2_WRKZ,
        Algorithm.AR2_CHUKWA,
        Algorithm.AR2_CHUKWA_V2,
    };

    #region Context managment

    internal static BlockingCollection<Context> contexts = null;

    public class Context : IDisposable
    {
        public Context()
        {
            handle = new Lazy<IntPtr>(alloc_context);
        }

        private Lazy<IntPtr> handle;

        public bool IsValid => handle.IsValueCreated && handle.Value != IntPtr.Zero;
        public IntPtr Handle => handle.Value;

        public void Dispose()
        {
            if(IsValid)
            {
                free_context(handle.Value);
                handle = null;
            }
        }
    }

    private readonly struct ContextLease : IDisposable
    {
        public ContextLease()
        {
            Context = contexts.Take();
        }

        public Context Context { get; }

        public void Dispose()
        {
            contexts.Add(Context);
        }
    }

    public static void InitContexts(int maxParallelism)
    {
        contexts = new BlockingCollection<Context>();

        for(var i=0;i<maxParallelism;i++)
            contexts.Add(new Context());
    }

    #endregion // Context managment

    public static void CryptonightHash(ReadOnlySpan<byte> data, Span<byte> result, Algorithm algo, ulong height)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32);
        Contract.Requires<ArgumentException>(validCryptonightAlgos.Contains(algo));

        var sw = Stopwatch.StartNew();

        fixed (byte* input = data)
        {
            fixed (byte* output = result)
            {
                using(var lease = new ContextLease())
                {
                    var success = cryptonight(input, data.Length, output, algo, height, lease.Context.Handle);
                    Debug.Assert(success);

                    messageBus?.SendTelemetry(algo.ToString(), TelemetryCategory.Hash, sw.Elapsed, true);
                }
            }
        }
    }

    public static void CryptonightLiteHash(ReadOnlySpan<byte> data, Span<byte> result, Algorithm algo, ulong height)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32);
        Contract.Requires<ArgumentException>(validCryptonightLiteAlgos.Contains(algo));

        var sw = Stopwatch.StartNew();

        fixed (byte* input = data)
        {
            fixed (byte* output = result)
            {
                using(var lease = new ContextLease())
                {
                    var success = cryptonight_lite(input, data.Length, output, algo, height, lease.Context.Handle);
                    Debug.Assert(success);

                    messageBus?.SendTelemetry(algo.ToString(), TelemetryCategory.Hash, sw.Elapsed, true);
                }
            }
        }
    }

    public static void CryptonightHeavyHash(ReadOnlySpan<byte> data, Span<byte> result, Algorithm algo, ulong height)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32);
        Contract.Requires<ArgumentException>(validCryptonightHeavyAlgos.Contains(algo));

        var sw = Stopwatch.StartNew();

        fixed (byte* input = data)
        {
            fixed (byte* output = result)
            {
                using(var lease = new ContextLease())
                {
                    var success = cryptonight_heavy(input, data.Length, output, algo, height, lease.Context.Handle);
                    Debug.Assert(success);

                    messageBus?.SendTelemetry(algo.ToString(), TelemetryCategory.Hash, sw.Elapsed, true);
                }
            }
        }
    }

    public static void CryptonightPicoHash(ReadOnlySpan<byte> data, Span<byte> result, Algorithm algo, ulong height)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32);
        Contract.Requires<ArgumentException>(validCryptonightPicoAlgos.Contains(algo));

        var sw = Stopwatch.StartNew();

        fixed (byte* input = data)
        {
            fixed (byte* output = result)
            {
                using(var lease = new ContextLease())
                {
                    var success = cryptonight_pico(input, data.Length, output, algo, height, lease.Context.Handle);
                    Debug.Assert(success);

                    messageBus?.SendTelemetry(algo.ToString(), TelemetryCategory.Hash, sw.Elapsed, true);
                }
            }
        }
    }

    public static void ArgonHash(ReadOnlySpan<byte> data, Span<byte> result, Algorithm algo, ulong height)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32);
        Contract.Requires<ArgumentException>(validArgonAlgos.Contains(algo));

        var sw = Stopwatch.StartNew();

        fixed (byte* input = data)
        {
            fixed (byte* output = result)
            {
                using(var lease = new ContextLease())
                {
                    var success = argon(input, data.Length, output, algo, height, lease.Context.Handle);
                    Debug.Assert(success);

                    messageBus?.SendTelemetry(algo.ToString(), TelemetryCategory.Hash, sw.Elapsed, true);
                }
            }
        }
    }
}
