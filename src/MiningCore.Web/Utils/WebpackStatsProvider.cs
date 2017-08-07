using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Newtonsoft.Json.Linq;

namespace MiningCore.Utils
{
    public class WebpackStatsProvider
    {
        public WebpackStatsProvider(IHostingEnvironment env)
        {
            var path = Path.Combine(env.WebRootPath, "build", "_wpstats.json");

            if (File.Exists(path))
            {
                stats = JObject.Parse(File.ReadAllText(path));
            }
        }

        private readonly JObject stats;


        private Func<string> uploadWorkerHash = null;

        public string UploadWorkerHash
        {
            get
            {
                if (uploadWorkerHash == null)
                {
                    try
                    {
                        var result = stats["chunks"].Children()
                            .First(x => x["names"].Any(y => y.Value<string>() == "upload-worker"))?["hash"]?
                            .Value<string>();

                        uploadWorkerHash = () => result;
                    }
                    catch (Exception)
                    {
                        uploadWorkerHash = () => DateTime.UtcNow.Ticks.ToString();
                    }
                }

                return uploadWorkerHash();
            }
        }
    }
}
