using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Xunit;

namespace MiningCore.Tests.Stratum
{
    /*
    public class StratumServerTests : TestBase
    {
        private readonly IPEndPoint listenEndPoint = new IPEndPoint(IPAddress.Loopback, 3032);

        [Fact]
        public async Task StratumServer_Should_Receive_Connect_On_Client_Connect()
        {
            using (var server = new TestStratumServer())
            {
                server.StartListeners(listenEndPoint);

                using (var tcpClient = await server.ConnectClient(listenEndPoint))
                {
                    await server.Connects.Take(1).ToTask();

                    tcpClient.Close();
                }

                Assert.Equal(1, server.ConnectCount);
            }
        }

        [Fact]
        public async Task StratumServer_Should_Receive_ReceiveCompleted_On_Client_Disconnect()
        {
            using (var server = new TestStratumServer())
            {
                server.StartListeners(listenEndPoint);

                using (var tcpClient = await server.ConnectClient(listenEndPoint))
                {
                    tcpClient.Close();
                }

                await server.ReceiveCompleted.Take(1).ToTask();
                Assert.Equal(1, server.ReceiveCompletedCount);
            }
        }

        [Fact]
        public async Task Disconnecting_StratumClient_Drops_Connection()
        {
            using (var server = new TestStratumServer())
            {
                server.StartListeners(listenEndPoint);

                using (var tcpClient = await server.ConnectClient(listenEndPoint))
                {
                    var client = await server.Connects.Take(1).ToTask();
                    client.Disconnect();

                    await server.ReceiveCompleted.Take(1).ToTask();
                    Assert.Equal(1, server.ReceiveCompletedCount);

                    tcpClient.Close();
                }
            }
        }
    }
    */
}
