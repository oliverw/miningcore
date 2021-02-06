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

            var app = new CommandLineApplication(false)
            {
                FullName = "MiningCore - Pool Mining Engine",
                ShortVersionGetter = () => $"v{Assembly.GetEntryAssembly().GetName().Version}",
                LongVersionGetter = () => $"v{Assembly.GetEntryAssembly().GetName().Version}"
            };

            var versionOption = app.Option("-v|--version", "Version Information", CommandOptionType.NoValue);
            var configFileOption = app.Option("-c|--config <configfile>", "Configuration File", CommandOptionType.SingleValue);
            dumpConfigOption = app.Option("-dc|--dumpconfig", "Dump the configuration (useful for trouble-shooting typos in the config file)", CommandOptionType.NoValue);
            shareRecoveryOption = app.Option("-rs", "Import lost shares using existing recovery file", CommandOptionType.SingleValue);
            app.HelpOption("-? | -h | --help");

            app.Execute(args);

            if( versionOption.HasValue() )
            {
                app.ShowVersion();
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
            if( shareRecoveryOption.HasValue())
            {
                PoolCore.Pool.RecoverSharesAsync(shareRecoveryOption.Value()).Wait();
            }
            
            if(!configFileOption.HasValue())
            {
                app.ShowHelp();
            }
            else
            {
                // Start Miningcore PoolCore
                PoolCore.Pool.Start(configFile);
            }

            

        }
        

    }
}
