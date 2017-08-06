using System.Runtime.InteropServices;

namespace MiningForce.Native
{
	// ReSharper disable once InconsistentNaming
    public static unsafe class libmultihash
    {
	    [DllImport("libmultihash", EntryPoint = "scrypt_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int scrypt(byte* input, byte* output, uint n, uint r, uint inputLength);

	    [DllImport("libmultihash", EntryPoint = "quark_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int quark(byte* input, byte* output, uint inputLength);

	    [DllImport("libmultihash", EntryPoint = "x11_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int x11(byte* input, byte* output, uint inputLength);

	    [DllImport("libmultihash", EntryPoint = "x15_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int x15(byte* input, byte* output, uint inputLength);

	    [DllImport("libmultihash", EntryPoint = "neoscrypt_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int neoscrypt(byte* input, byte* output, uint inputLength, int profile);

	    [DllImport("libmultihash", EntryPoint = "scryptn_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int scryptn(byte* input, byte* output, uint nFactor, uint inputLength);

	    [DllImport("libmultihash", EntryPoint = "kezzak_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int kezzak(byte* input, byte* output, uint inputLength);

	    [DllImport("libmultihash", EntryPoint = "bcrypt_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int bcrypt(byte* input, byte* output, uint inputLength);

	    [DllImport("libmultihash", EntryPoint = "skein_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int skein(byte* input, byte* output, uint inputLength);

	    [DllImport("libmultihash", EntryPoint = "groestl_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int groestl(byte* input, byte* output, uint inputLength);

	    [DllImport("libmultihash", EntryPoint = "groestl_myriad_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int groestl_myriad(byte* input, byte* output, uint inputLength);

	    [DllImport("libmultihash", EntryPoint = "blake_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int blake(byte* input, byte* output, uint inputLength);

	    [DllImport("libmultihash", EntryPoint = "dcrypt_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int dcrypt(byte* input, byte* output, uint inputLength);

	    [DllImport("libmultihash", EntryPoint = "fugue_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int fugue(byte* input, byte* output, uint inputLength);

	    [DllImport("libmultihash", EntryPoint = "qubit_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int qubit(byte* input, byte* output, uint inputLength);

	    [DllImport("libmultihash", EntryPoint = "s3_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int s3(byte* input, byte* output, uint inputLength);

	    [DllImport("libmultihash", EntryPoint = "hefty1_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int hefty1(byte* input, byte* output, uint inputLength);

	    [DllImport("libmultihash", EntryPoint = "shavite3_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int shavite3(byte* input, byte* output, uint inputLength);

	    [DllImport("libmultihash", EntryPoint = "nist5_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int nist5(byte* input, byte* output, uint inputLength);

	    [DllImport("libmultihash", EntryPoint = "fresh_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int fresh(byte* input, byte* output, uint inputLength);

	    [DllImport("libmultihash", EntryPoint = "jh_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int jh(byte* input, byte* output, uint inputLength);

	    [DllImport("libmultihash", EntryPoint = "c11_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int c11(byte* input, byte* output, uint inputLength);
	}
}
