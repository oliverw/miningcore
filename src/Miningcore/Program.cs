using McMaster.Extensions.CommandLineUtils;
using Miningcore.Configuration;
using Miningcore.PoolCore;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Miningcore
{
    public class Program
    {
        private static CommandOption dumpConfigOption;
        private static CommandOption shareRecoveryOption;
        private static ClusterConfig clusterConfig;

        public static void Main(string[] args)
        {
            string configFile = "config_template.json";

            PoolLogo.Logo();

            var MiningCore = new CommandLineApplication(throwOnUnexpectedArg: false)
            {
                Name = "dotnet Miningcore.dll",
                FullName = "MiningCore 2.0 - Mining Pool Engine",
                Description = "Stratum mining pool engine for Bitcoin and Altcoins",
                ShortVersionGetter = () => $"- MinerNL v{Assembly.GetEntryAssembly().GetName().Version}",
                LongVersionGetter = () => $"- MinerNL v{Assembly.GetEntryAssembly().GetName().Version}",
                ExtendedHelpText = "--------------------------------------------------------------------------------------------------------------"
            };


            var versionOption = MiningCore.Option("-v|--version", "Version Information", CommandOptionType.NoValue);
            var configFileOption = MiningCore.Option("-c|--config <configfile>", "Configuration File", CommandOptionType.SingleValue);
            dumpConfigOption = MiningCore.Option("-dc|--dumpconfig", "Dump the configuration (useful for trouble-shooting typos in the config file)", CommandOptionType.NoValue);
            shareRecoveryOption = MiningCore.Option("-rs", "Import lost shares using existing recovery file", CommandOptionType.SingleValue);
            MiningCore.HelpOption("-? | -h | --help");
            MiningCore.OnExecute(() =>
            {
                // Display Software Version
                Assembly thisAssem = typeof(Program).Assembly;
                AssemblyName thisAssemName = thisAssem.GetName();
                Version ver = thisAssemName.Version;
                Console.WriteLine("-----------------------------------------------------------------------------------------------------------------------");
                Console.WriteLine($"Running Miningcore V{ver}");

                if(versionOption.HasValue())
                {
                    MiningCore.ShowVersion();
                }

                // overwrite default config_template.json with -c | --config <configfile> file
                if(configFileOption.HasValue())
                {
                    configFile = configFileOption.Value();
                }

                // Dump Config to JSON output
                if(dumpConfigOption.HasValue())
                {
                    clusterConfig = PoolCore.PoolConfig.GetConfigContent(configFile);
                    PoolCore.PoolConfig.DumpParsedConfig(clusterConfig);
                }

                // Shares recovery from file to database
                if(shareRecoveryOption.HasValue())
                {
                    PoolCore.Pool.RecoverSharesAsync(shareRecoveryOption.Value()).Wait();
                }

                if(!configFileOption.HasValue())
                {
                    MiningCore.ShowHelp();
                }
                else
                {
                    // Start Miningcore PoolCore
                    PoolCore.Pool.Start(configFile);
                }
                return 0;

            });
            MiningCore.Execute(args);

        }

    }
}
