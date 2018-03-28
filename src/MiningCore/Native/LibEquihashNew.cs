using System;
using System.Runtime.InteropServices;

namespace MiningCore.Native
{
    public static unsafe class LibEquihashVerifyNew
    {
        [DllImport("libequihashverifynew", EntryPoint = "equihash_verify_new_export", Callingconvention = Callingconvention.CDecl)]
        public static extern bool equihash_verify_new(byte* header, int header_length, byte* solution, int solution_length);
    }
}
