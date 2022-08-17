using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Miningcore.Blockchain.Bamboo;

unsafe class Unmanaged
{
    [DllImport("Blockchain/Bamboo/pufferfish2", ExactSpelling = true)]
    [SuppressGCTransition]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static extern int pf_newhash(byte* pass, int pass_sz, byte* hash);

    [DllImport("Blockchain/Bamboo/sign", ExactSpelling = true)]
    [SuppressGCTransition]
    public static extern void ed25519_sign(byte* signature, byte* message, int message_len, byte* public_key, byte* private_key);
}
