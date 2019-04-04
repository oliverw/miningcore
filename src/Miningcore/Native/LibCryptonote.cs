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
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using Miningcore.Contracts;

namespace Miningcore.Native
{
    public static unsafe class LibCryptonote
    {
        [DllImport("libcryptonote", EntryPoint = "convert_blob_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool convert_blob(byte* input, int inputSize, byte* output, ref int outputSize);

        [DllImport("libcryptonote", EntryPoint = "decode_address_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern UInt64 decode_address(byte* input, int inputSize);

        [DllImport("libcryptonote", EntryPoint = "decode_integrated_address_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern UInt64 decode_integrated_address(byte* input, int inputSize);

        [DllImport("libcryptonote", EntryPoint = "cn_fast_hash_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern int cn_fast_hash(byte* input, byte* output, uint inputLength);

        public static byte[] ConvertBlob(ReadOnlySpan<byte> data, int size)
        {
            Contract.Requires<ArgumentException>(data.Length > 0, $"{nameof(data)} must not be empty");

            fixed (byte* input = data)
            {
                // provide reasonable large output buffer
                var outputBuffer = ArrayPool<byte>.Shared.Rent(0x100);

                try
                {
                    var outputBufferLength = outputBuffer.Length;

                    var success = false;
                    fixed (byte* output = outputBuffer)
                    {
                        success = convert_blob(input, size, output, ref outputBufferLength);
                    }

                    if(!success)
                    {
                        // if we get false, the buffer might have been too small
                        if(outputBufferLength == 0)
                            return null; // nope, other error

                        // retry with correctly sized buffer
                        ArrayPool<byte>.Shared.Return(outputBuffer);
                        outputBuffer = ArrayPool<byte>.Shared.Rent(outputBufferLength);

                        fixed (byte* output = outputBuffer)
                        {
                            success = convert_blob(input, size, output, ref outputBufferLength);
                        }

                        if(!success)
                            return null; // sorry
                    }

                    // build result buffer
                    var result = new byte[outputBufferLength];
                    Buffer.BlockCopy(outputBuffer, 0, result, 0, outputBufferLength);

                    return result;
                }

                finally
                {
                    ArrayPool<byte>.Shared.Return(outputBuffer);
                }
            }
        }

        public static UInt64 DecodeAddress(string address)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(address), $"{nameof(address)} must not be empty");

            var data = Encoding.UTF8.GetBytes(address);

            fixed (byte* input = data)
            {
                return decode_address(input, data.Length);
            }
        }

        public static UInt64 DecodeIntegratedAddress(string address)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(address), $"{nameof(address)} must not be empty");

            var data = Encoding.UTF8.GetBytes(address);

            fixed (byte* input = data)
            {
                return decode_integrated_address(input, data.Length);
            }
        }

        public static void CryptonightHashFast(ReadOnlySpan<byte> data, Span<byte> result)
        {
            Contract.Requires<ArgumentException>(result.Length >= 32, $"{nameof(result)} must be greater or equal 32 bytes");

            fixed (byte* input = data)
            {
                fixed (byte* output = result)
                {
                    cn_fast_hash(input, output, (uint) data.Length);
                }
            }
        }
    }
}
