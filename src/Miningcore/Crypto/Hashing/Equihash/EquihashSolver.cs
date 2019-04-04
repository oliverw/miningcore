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
using System.Threading;
using Miningcore.Native;

// ReSharper disable InconsistentNaming

namespace Miningcore.Crypto.Hashing.Equihash
{
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

        protected static readonly Lazy<Semaphore> sem = new Lazy<Semaphore>(() =>
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
            try
            {
                sem.Value.WaitOne();

                fixed (byte* h = header)
                {
                    fixed (byte* s = solution)
                    {
                        return LibMultihash.equihash_verify_200_9(h, header.Length, s, solution.Length, personalization);
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
            try
            {
                sem.Value.WaitOne();

                fixed (byte* h = header)
                {
                    fixed (byte* s = solution)
                    {
                        return LibMultihash.equihash_verify_144_5(h, header.Length, s, solution.Length, personalization);
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
            try
            {
                sem.Value.WaitOne();

                fixed (byte* h = header)
                {
                    fixed (byte* s = solution)
                    {
                        return LibMultihash.equihash_verify_96_5(h, header.Length, s, solution.Length, personalization);
                    }
                }
            }

            finally
            {
                sem.Value.Release();
            }
        }
    }
}
