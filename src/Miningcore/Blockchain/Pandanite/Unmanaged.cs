using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Miningcore.Blockchain.Pandanite;

unsafe class Unmanaged
{
    [DllImport("Blockchain/Pandanite/pufferfish2", ExactSpelling = true)]
    [SuppressGCTransition]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static extern int pf_newhash(byte* pass, int pass_sz, byte* hash);

    [DllImport("Blockchain/Pandanite/sign", ExactSpelling = true)]
    [SuppressGCTransition]
    public static extern void ed25519_sign(byte* signature, byte* message, int message_len, byte* public_key, byte* private_key);
}
