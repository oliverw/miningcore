using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace net_core_test
{
    unsafe class Program
    {
	    [DllImport("libmultihash", EntryPoint = "scrypt_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int scrypt(byte* input, byte* output, uint n, uint r, uint inputLength);

	    public static string ToHexString(IEnumerable<byte> byteArray)
	    {
		    return ToHexString(byteArray.ToArray());
	    }

	    public static string ToHexString(byte[] byteArray)
	    {
		    return byteArray.Aggregate("", (current, b) => current + b.ToString("x2"));
	    }

		public static byte[] HexToByteArray(string str)
	    {
		    byte[] arr = new byte[str.Length >> 1];

		    for (int i = 0; i < (str.Length >> 1); ++i)
		    {
				arr[i] = (byte)((GetHexVal(str[i << 1]) << 4) + (GetHexVal(str[(i << 1) + 1])));
		    }

		    return arr;
	    }

		private static int GetHexVal(char hex)
	    {
		    int val = (int)hex;
		    return val - (val < 58 ? 48 : (val < 97 ? 55 : 87));
	    }

		static void Main(string[] args)
		{
			var data = HexToByteArray("{A16A29DF-C291-482A-A739-A105A2F240C9}");
	        var result = new byte[32];

	        fixed (byte* input = data)
	        {
		        fixed (byte* output = result)
		        {
			        scrypt(input, output, 1024, 1, (uint)data.Length);
		        }

		        Console.WriteLine(ToHexString(result));
			}
		}
    }
}
