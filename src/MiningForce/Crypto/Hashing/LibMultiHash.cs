using System.Runtime.InteropServices;

namespace MiningForce.Crypto.Hashing
{
    public static unsafe class LibMultiHash
    {
	    [DllImport("multihash-native", EntryPoint = "scrypt_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int scrypt(byte* input, byte* output, uint n, uint r, uint inputLength);

	    [DllImport("multihash-native", EntryPoint = "quark_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int quark(byte* input, byte* output, uint inputLength);

	    [DllImport("multihash-native", EntryPoint = "x11_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int x11(byte* input, byte* output, uint inputLength);

	    [DllImport("multihash-native", EntryPoint = "x15_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int x15(byte* input, byte* output, uint inputLength);

	    [DllImport("multihash-native", EntryPoint = "neoscrypt_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int neoscrypt(byte* input, byte* output, uint inputLength, int profile);

	    [DllImport("multihash-native", EntryPoint = "scryptn_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int scryptn(byte* input, byte* output, uint nFactor, uint inputLength);

	    [DllImport("multihash-native", EntryPoint = "kezzak_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int kezzak(byte* input, byte* output, uint inputLength);

	    [DllImport("multihash-native", EntryPoint = "bcrypt_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int bcrypt(byte* input, byte* output, uint inputLength);

	    [DllImport("multihash-native", EntryPoint = "skein_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int skein(byte* input, byte* output, uint inputLength);

	    [DllImport("multihash-native", EntryPoint = "groestl_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int groestl(byte* input, byte* output, uint inputLength);

	    [DllImport("multihash-native", EntryPoint = "groestl_myriad_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int groestl_myriad(byte* input, byte* output, uint inputLength);

	    [DllImport("multihash-native", EntryPoint = "blake_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int blake(byte* input, byte* output, uint inputLength);

	    [DllImport("multihash-native", EntryPoint = "dcrypt_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int dcrypt(byte* input, byte* output, uint inputLength);

	    [DllImport("multihash-native", EntryPoint = "fugue_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int fugue(byte* input, byte* output, uint inputLength);

	    [DllImport("multihash-native", EntryPoint = "qubit_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int qubit(byte* input, byte* output, uint inputLength);

	    [DllImport("multihash-native", EntryPoint = "s3_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int s3(byte* input, byte* output, uint inputLength);

	    [DllImport("multihash-native", EntryPoint = "hefty1_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int hefty1(byte* input, byte* output, uint inputLength);

	    [DllImport("multihash-native", EntryPoint = "shavite3_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int shavite3(byte* input, byte* output, uint inputLength);

	    [DllImport("multihash-native", EntryPoint = "cryptonight_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int cryptonight(byte* input, byte* output, uint inputLength, bool fast);

	    [DllImport("multihash-native", EntryPoint = "nist5_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int nist5(byte* input, byte* output, uint inputLength);

	    [DllImport("multihash-native", EntryPoint = "fresh_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int fresh(byte* input, byte* output, uint inputLength);

	    [DllImport("multihash-native", EntryPoint = "jh_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int jh(byte* input, byte* output, uint inputLength);

	    [DllImport("multihash-native", EntryPoint = "c11_export", CallingConvention = CallingConvention.Cdecl)]
	    public static extern int c11(byte* input, byte* output, uint inputLength);
	}
}
