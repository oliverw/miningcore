using System;
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

            // Run pool for 120 secs
            CancellationTokenSource cts = new CancellationTokenSource();
            var timeoutMs = 120000;
            cts.CancelAfter(timeoutMs);

            try
            {
                await Program.Start(new string[]{"-c", "config_test.json"}, cts.Token);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException || ex is TaskCanceledException)
                {
                    // Ignore
                }

                throw;
            }

            // Validate if shares were processed successfully
            Assert.Equal(0, await DataHelper.GetUnProcessedSharesCountAsync());
            Assert.NotNull(await DataHelper.GetBalanceAsync());
        }
    }
}
