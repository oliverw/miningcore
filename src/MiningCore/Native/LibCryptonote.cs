using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MiningCore.Native
{
    public static unsafe class LibCryptonote
	{
	    [DllImport("libcryptonote", EntryPoint = "convert_blob_export", CallingConvention = CallingConvention.Cdecl)]
	    private static extern bool convert_blob(byte* input, int inputSize, byte* output, ref int outputSize);

		[DllImport("libcryptonote", EntryPoint = "decode_address_export", CallingConvention = CallingConvention.Cdecl)]
		private static extern uint decode_address(byte* input, int inputSize);

		[DllImport("libcryptonote", EntryPoint = "cn_slow_hash_export", CallingConvention = CallingConvention.Cdecl)]
		private static extern int cn_slow_hash(byte* input, byte* output, uint inputLength);

		[DllImport("libcryptonote", EntryPoint = "cn_fast_hash_export", CallingConvention = CallingConvention.Cdecl)]
		private static extern int cn_fast_hash(byte* input, byte* output, uint inputLength);

		public static byte[] ConvertBlob(byte[] data)
		{
			fixed (byte* input = data)
			{
				// provide reasonable large output buffer
				var outputBuffer = new byte[0x100];
				var outputBufferLength = outputBuffer.Length;

				bool success = false;
				fixed (byte* output = outputBuffer)
				{
					success = convert_blob(input, data.Length, output, ref outputBufferLength);
				}

				if (!success)
				{
					// if we get false, the buffer might have been too small
					if (outputBufferLength == 0)
						return null;	// nope, other error

					// retry with correctly sized buffer
					outputBuffer = new byte[outputBufferLength];

					fixed (byte* output = outputBuffer)
					{
						success = convert_blob(input, data.Length, output, ref outputBufferLength);
					}

					if (!success)
						return null;	// sorry
				}

				// build result buffer
				var result = new byte[outputBufferLength];
				Buffer.BlockCopy(outputBuffer, 0, result, 0, outputBufferLength);

				return result;
			}
		}

		public static uint DecodeAddress(string address)
		{
			var data = Encoding.UTF8.GetBytes(address);

			fixed (byte* input = data)
			{
				return decode_address(input, data.Length);
			}
		}

		public static byte[] CryptonightHashSlow(byte[] data)
		{
			var result = new byte[32];

			fixed (byte* input = data)
			{
				fixed (byte* output = result)
				{
					cn_slow_hash(input, output, (uint)data.Length);
				}
			}

			return result;
		}

		public static byte[] CryptonightHashFast(byte[] data)
		{
			var result = new byte[32];

			fixed (byte* input = data)
			{
				fixed (byte* output = result)
				{
					cn_fast_hash(input, output, (uint)data.Length);
				}
			}

			return result;
		}
	}
}
