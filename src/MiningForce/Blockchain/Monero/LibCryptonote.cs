using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MiningForce.Blockchain.Monero
{
    public static unsafe class LibCryptoNote
	{
	    [DllImport("libcryptonote", EntryPoint = "convert_blob_export", CallingConvention = CallingConvention.Cdecl)]
	    private static extern bool convert_blob(byte* input, int inputSize, byte* output, ref int outputSize);

		[DllImport("libcryptonote", EntryPoint = "decode_address_export", CallingConvention = CallingConvention.Cdecl)]
		private static extern bool decode_address(byte* input, int inputSize, byte* output, ref int outputSize);

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

		public static byte[] DecodeAddress(string address)
		{
			var data = Encoding.UTF8.GetBytes(address);

			fixed (byte* input = data)
			{
				// provide reasonable large output buffer
				var outputBuffer = new byte[0x100];
				var outputBufferLength = outputBuffer.Length;

				bool success = false;
				fixed (byte* output = outputBuffer)
				{
					success = decode_address(input, data.Length, output, ref outputBufferLength);
				}

				if (!success)
				{
					// if we get false, the buffer might have been too small
					if (outputBufferLength == 0)
						return null;    // nope, other error

					// retry with correctly sized buffer
					outputBuffer = new byte[outputBufferLength];

					fixed (byte* output = outputBuffer)
					{
						success = decode_address(input, data.Length, output, ref outputBufferLength);
					}

					if (!success)
						return null;    // sorry
				}

				// build result buffer
				var result = new byte[outputBufferLength];
				Buffer.BlockCopy(outputBuffer, 0, result, 0, outputBufferLength);

				return result;
			}
		}
	}
}
