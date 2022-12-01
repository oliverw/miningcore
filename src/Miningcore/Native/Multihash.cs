using System.Runtime.InteropServices;

namespace Miningcore.Native;

public static unsafe class Multihash
{
    [DllImport("libmultihash", EntryPoint = "scrypt_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void scrypt(byte* input, void* output, uint n, uint r, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "quark_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void quark(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "sha256csm_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void sha256csm(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "sha3_256_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void sha3_256(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "sha3_512_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void sha3_512(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "hmq17_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void hmq17(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "phi_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void phi(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "x11_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void x11(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "x13_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void x13(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "x13_bcd_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void x13_bcd(byte* input, void* output);

    [DllImport("libmultihash", EntryPoint = "x17_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void x17(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "x21s_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void x21s(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "x22i_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void x22i(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "x25x_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void x25x(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "verthash_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern int verthash(byte* input, void* output, int inputLength);

    [DllImport("libmultihash", EntryPoint = "verthash_init_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern int verthash_init(string filename, bool createIfMissing);

    [DllImport("libmultihash", EntryPoint = "neoscrypt_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void neoscrypt(byte* input, void* output, uint inputLength, uint profile);

    [DllImport("libmultihash", EntryPoint = "scryptn_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void scryptn(byte* input, void* output, uint nFactor, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "kezzak_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void kezzak(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "bcrypt_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void bcrypt(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "skein_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void skein(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "groestl_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void groestl(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "groestl_myriad_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void groestl_myriad(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "blake_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void blake(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "blake2s_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void blake2s(byte* input, void* output, uint inputLength, int outputLength = -1);

    [DllImport("libmultihash", EntryPoint = "blake2b_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void blake2b(byte* input, void* output, uint inputLength, int outputLength);

    [DllImport("libmultihash", EntryPoint = "dcrypt_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void dcrypt(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "fugue_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void fugue(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "qubit_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void qubit(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "heavyhash_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void heavyhash(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "s3_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void s3(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "hefty1_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void hefty1(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "shavite3_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void shavite3(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "nist5_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void nist5(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "fresh_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void fresh(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "jh_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void jh(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "c11_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void c11(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "x16r_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void x16r(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "x16rv2_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void x16rv2(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "x16s_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void x16s(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "geek_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void geek(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "lyra2re_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void lyra2re(byte* input, void* output);

    [DllImport("libmultihash", EntryPoint = "lyra2rev2_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void lyra2rev2(byte* input, void* output);

    [DllImport("libmultihash", EntryPoint = "lyra2rev3_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void lyra2rev3(byte* input, void* output);

    [DllImport("libmultihash", EntryPoint = "equihash_verify_200_9_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool equihash_verify_200_9(byte* header, int headerLength, byte* solution, int solutionLength, string personalization);

    [DllImport("libmultihash", EntryPoint = "equihash_verify_144_5_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool equihash_verify_144_5(byte* header, int headerLength, byte* solution, int solutionLength, string personalization);

    [DllImport("libmultihash", EntryPoint = "equihash_verify_96_5_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool equihash_verify_96_5(byte* header, int headerLength, byte* solution, int solutionLength, string personalization);

    [DllImport("libmultihash", EntryPoint = "sha512_256_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void sha512_256(byte* input, void* output, uint inputLength);

    [DllImport("libmultihash", EntryPoint = "sha256dt_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void sha256dt(byte* input, void* output);
}
