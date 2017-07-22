using System.Reactive.Linq;
using System.Threading.Tasks;
using MiningForce.JsonRpc;
using MiningForce.Tests.Util;
using Xunit;

namespace MiningForce.Tests.JsonRpc
{
    public class JsonRpcConnectionTests
    {
        [Fact]
        public async Task Closing_Connection_Should_Close_Upstream()
        {
			var upstream = new MockLibUvConnection();

			var con = new JsonRpcConnection(Globals.JsonSerializerSettings);
			con.Init(upstream);
			con.Close();

	        await upstream.Closed.Take(1);
        }
	}
}
