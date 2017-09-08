using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.Tests
{
    public static class ModuleInitializer
    {
        private static readonly object initLock = new object();

        private static bool isInitialized = false;

        /// <summary>
        /// Initializes the module.
        /// </summary>
        public static void Initialize()
        {
            lock (initLock)
            {
                if (isInitialized)
                    return;

                Program.PreloadNativeLibs();

                isInitialized = true;
            }
        }
    }
}
