using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Miningcore.Integration.Tests.Ethereum
{
    public class PayoutTests : TestBase
    {
        [Fact]
        public async Task BalanceCalculation()
        {
            // Start fresh
            await DataHelper.CleanupShares();

            // Data setup
            await DataHelper.AddTestSharesAsync();
            await DataHelper.AddPoolStateAsync();
            await DataHelper.AddPoolStatisticsAsync();

            // Run pool for 30 secs
            CancellationTokenSource cts = new CancellationTokenSource();
            var timeoutMs = 60000;
            cts.CancelAfter(timeoutMs);
            Task task = Program.Start(new string[]{"-c", "config_test.json"}, cts.Token);
            
            // Wait for the pool to timeout
            Thread.Sleep(timeoutMs);

            // Validate if shares were processed successfully
            Assert.Equal(0, await DataHelper.GetUnProcessedSharesCountAsync());
            Assert.NotNull(await DataHelper.GetBalanceAsync());
        }
    }
}
