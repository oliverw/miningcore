using System.Diagnostics;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Native;
using Miningcore.Notifications.Messages;

// ReSharper disable InconsistentNaming

namespace Miningcore.Crypto.Hashing.Equihash;

public abstract class EquihashSolver
{
    private static int maxThreads = 1;

    public static int MaxThreads
    {
        get => maxThreads;
        set
        {
            if(sem.IsValueCreated)
                throw new InvalidOperationException("Too late: semaphore already created");

            maxThreads = value;
        }
    }

    internal static IMessageBus messageBus;

    protected static readonly Lazy<Semaphore> sem = new(() =>
        new Semaphore(maxThreads, maxThreads));

    protected string personalization;

    public string Personalization => personalization;

    /// <summary>
    /// Verify an Equihash solution
    /// </summary>
    /// <param name="header">header including nonce (140 bytes)</param>
    /// <param name="solution">equihash solution without size-preamble</param>
    /// <returns></returns>
    public abstract bool Verify(ReadOnlySpan<byte> header, ReadOnlySpan<byte> solution);
}

public unsafe class EquihashSolver_200_9 : EquihashSolver
{
    public EquihashSolver_200_9(string personalization)
    {
        this.personalization = personalization;
    }

    public override bool Verify(ReadOnlySpan<byte> header, ReadOnlySpan<byte> solution)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            sem.Value.WaitOne();

            fixed (byte* h = header)
            {
                fixed (byte* s = solution)
                {
                    var result = Multihash.equihash_verify_200_9(h, header.Length, s, solution.Length, personalization);

                    messageBus?.SendTelemetry("Equihash 200-9", TelemetryCategory.Hash, sw.Elapsed, result);

                    return result;
                }
            }
        }

        finally
        {
            sem.Value.Release();
        }
    }
}

public unsafe class EquihashSolver_144_5 : EquihashSolver
{
    public EquihashSolver_144_5(string personalization)
    {
        this.personalization = personalization;
    }

    public override bool Verify(ReadOnlySpan<byte> header, ReadOnlySpan<byte> solution)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            sem.Value.WaitOne();

            fixed (byte* h = header)
            {
                fixed (byte* s = solution)
                {
                    var result = Multihash.equihash_verify_144_5(h, header.Length, s, solution.Length, personalization);

                    messageBus?.SendTelemetry(personalization ?? "Equihash 144-5", TelemetryCategory.Hash, sw.Elapsed, result);

                    return result;
                }
            }
        }

        finally
        {
            sem.Value.Release();
        }
    }
}

public unsafe class EquihashSolver_96_5 : EquihashSolver
{
    public EquihashSolver_96_5(string personalization)
    {
        this.personalization = personalization;
    }

    public override bool Verify(ReadOnlySpan<byte> header, ReadOnlySpan<byte> solution)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            sem.Value.WaitOne();

            fixed (byte* h = header)
            {
                fixed (byte* s = solution)
                {
                    var result = Multihash.equihash_verify_96_5(h, header.Length, s, solution.Length, personalization);

                    messageBus?.SendTelemetry("Equihash 96-5", TelemetryCategory.Hash, sw.Elapsed, result);

                    return result;
                }
            }
        }

        finally
        {
            sem.Value.Release();
        }
    }
}
