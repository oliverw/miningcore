using System.IO;
using Miningcore.Configuration;
using Miningcore.Integration.Tests.Helpers;
using Newtonsoft.Json;

namespace Miningcore.Integration.Tests
{
    public abstract class TestBase
    {
        protected TestBase()
        {
            TestAppConfig = JsonConvert.DeserializeObject<ClusterConfig>(File.ReadAllText("config_test.json"));
            DataHelper = new DataHelper(TestAppConfig.Persistence);
        }

        public ClusterConfig TestAppConfig { get; }

        public DataHelper DataHelper { get; }
    }
}
