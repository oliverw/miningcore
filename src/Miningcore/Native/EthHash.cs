using System.Runtime.InteropServices;

// ReSharper disable InconsistentNaming

namespace Miningcore.Native;

public static unsafe class EthHash
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ethash_h256_t
    {
        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U8, SizeConst = 32)] public byte[] value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ethash_return_value
    {
        public ethash_h256_t result;
        public ethash_h256_t mix_hash;

        [MarshalAs(UnmanagedType.U1)] public bool success;
    }

    public delegate int ethash_callback_t(uint progress);

    /// <summary>
    /// Allocate and initialize a new ethash_light handler
    /// </summary>
    /// <param name="block_number">The block number for which to create the handler</param>
    /// <returns>Newly allocated ethash_light handler or NULL</returns>
    [DllImport("libethhash", EntryPoint = "ethash_light_new_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr ethash_light_new(ulong block_number);

    /// <summary>
    /// Frees a previously allocated ethash_light handler
    /// </summary>
    /// <param name="handle">The light handler to free</param>
    [DllImport("libethhash", EntryPoint = "ethash_light_delete_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ethash_light_delete(IntPtr handle);

    /// <summary>
    /// Calculate the light client data
    /// </summary>
    /// <param name="handle">The light client handler</param>
    /// <param name="header_hash">The 32-Byte header hash to pack into the mix</param>
    /// <param name="nonce">The nonce to pack into the mix</param>
    /// <returns>an object of ethash_return_value_t holding the return values</returns>
    [DllImport("libethhash", EntryPoint = "ethash_light_compute_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ethash_light_compute(IntPtr handle, byte* header_hash, ulong nonce, ref ethash_return_value result);

    /// <summary>
    /// Allocate and initialize a new ethash_full handler
    /// </summary>
    /// <param name="dagDir">Directory where generated DAGs reside</param>
    /// <param name="light">The light handler containing the cache.</param>
    /// <param name="callback">
    /// A callback function with signature of @ref ethash_callback_t
    /// It accepts an unsigned with which a progress of DAG calculation
    /// can be displayed. If all goes well the callback should return 0.
    /// If a non-zero value is returned then DAG generation will stop.
    /// Be advised. A progress value of 100 means that DAG creation is
    /// almost complete and that this function will soon return succesfully.
    /// It does not mean that the function has already had a succesfull return.
    /// </param>
    /// <returns></returns>
    [DllImport("libethhash", EntryPoint = "ethash_full_new_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr ethash_full_new(string dagDir, IntPtr light, ethash_callback_t callback);

    /// <summary>
    /// Frees a previously allocated ethash_full handler
    /// </summary>
    /// <param name="handle">The full handler to free</param>
    [DllImport("libethhash", EntryPoint = "ethash_full_delete_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ethash_full_delete(IntPtr handle);

    /// <summary>
    /// Calculate the full client data
    /// </summary>
    /// <param name="handle">The full client handler</param>
    /// <param name="header_hash">The 32-Byte header hash to pack into the mix</param>
    /// <param name="nonce">The nonce to pack into the mix</param>
    /// <returns>an object of ethash_return_value_t holding the return values</returns>
    [DllImport("libethhash", EntryPoint = "ethash_full_compute_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ethash_full_compute(IntPtr handle, byte* header_hash, ulong nonce, ref ethash_return_value result);

    /// <summary>
    /// Get a pointer to the full DAG data
    /// </summary>
    /// <param name="handle">The full handler to free</param>
    [DllImport("libethhash", EntryPoint = "ethash_full_dag_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr ethash_full_dag(IntPtr handle);

    /// <summary>
    /// Get the size of the DAG data
    /// </summary>
    /// <param name="handle">The full handler to free</param>
    [DllImport("libethhash", EntryPoint = "ethash_full_dag_size_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern ulong ethash_full_dag_size(IntPtr handle);

    /// <summary>
    /// Calculate the seedhash for a given block number
    /// </summary>
    /// <param name="handle">The full handler to free</param>
    [DllImport("libethhash", EntryPoint = "ethash_get_seedhash_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern ethash_h256_t ethash_get_seedhash(ulong block_number);

    /// <summary>
    /// Get the default DAG directory
    /// </summary>
    [DllImport("libethhash", EntryPoint = "ethash_get_default_dirname_export", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool ethash_get_default_dirname(byte* data, int length);
}
