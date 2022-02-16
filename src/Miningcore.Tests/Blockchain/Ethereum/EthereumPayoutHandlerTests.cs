using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Autofac.Features.Metadata;
using Miningcore.Blockchain.Ethereum;
using Miningcore.Configuration;
using Miningcore.Rpc;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Payments;
using Miningcore.Tests.Rpc;
using Moq;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Newtonsoft.Json;
using Xunit;

namespace Miningcore.Tests.Blockchain.Ethereum
{
    public class EthereumPayoutHandlerTests : TestBase
    {
        private const ulong TransferGas = 21000;
        private const decimal DefaultMinimumPayout = (decimal) 0.004;
        private readonly EthereumPayoutHandler ethereumPayoutHandler;
        private readonly IMessageBus messageBus;
        private readonly ClusterConfig clusterConfig;
        private readonly PoolConfig poolConfig;

        public EthereumPayoutHandlerTests()
        {
            var ctx = ModuleInitializer.Container;
            var handlerImpl = ctx.Resolve<IEnumerable<Meta<Lazy<IPayoutHandler, CoinFamilyAttribute>>>>()
                .First(x => x.Value.Metadata.SupportedFamilies.Contains(CoinFamily.Ethereum)).Value;

            clusterConfig = ctx.Resolve<ClusterConfig>();
            poolConfig = clusterConfig.Pools.First(c => c.Coin.Equals("ethereum", StringComparison.OrdinalIgnoreCase));
            ethereumPayoutHandler = handlerImpl.Value as EthereumPayoutHandler;
            messageBus = ctx.Resolve<IMessageBus>();
        }

        #region ConfigureOnDemandPayoutAsync

        [Fact]
        public async Task ConfigureOnDemandPayoutAsync_Successful()
        {
            var web3 = new Mock<IWeb3>();
            InitMockHandler(new MockRpcClient(), web3);

            ethereumPayoutHandler.ConfigureOnDemandPayoutAsync(CancellationToken.None);
            messageBus.NotifyNetworkBlock(poolConfig.Id, 180000000000, 10000000, poolConfig.Template);

            await Task.Delay(3000);
            web3.Verify(w => w.Eth.TransactionManager.TransactionReceiptService.PollForReceiptAsync(
                It.IsAny<string>(), It.IsAny<CancellationTokenSource>()), Times.AtLeastOnce());
            web3.Verify(w => w.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
            web3.Verify(w => w.Eth.Transactions.GetTransactionByHash.SendRequestAsync(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
        }

        [Fact]
        public async Task ConfigureOnDemandPayoutAsync_ZeroGasFee()
        {
            var web3 = new Mock<IWeb3>();
            InitMockHandler(new MockRpcClient(), web3);

            ethereumPayoutHandler.ConfigureOnDemandPayoutAsync(CancellationToken.None);
            messageBus.NotifyNetworkBlock(poolConfig.Id, 0, 10000000, poolConfig.Template);

            await Task.Delay(3000);
            web3.Verify(w => w.Eth.TransactionManager.TransactionReceiptService.PollForReceiptAsync(
                It.IsAny<string>(), It.IsAny<CancellationTokenSource>()), Times.Never);
        }

        [Fact]
        public async Task ConfigureOnDemandPayoutAsync_HighGasFee()
        {
            var web3 = new Mock<IWeb3>();
            InitMockHandler(new MockRpcClient(), web3);

            ethereumPayoutHandler.ConfigureOnDemandPayoutAsync(CancellationToken.None);
            messageBus.NotifyNetworkBlock(poolConfig.Id, 181000000000, 10000000, poolConfig.Template);

            await Task.Delay(3000);
            web3.Verify(w => w.Eth.TransactionManager.TransactionReceiptService.PollForReceiptAsync(
                It.IsAny<string>(), It.IsAny<CancellationTokenSource>()), Times.Never);
        }

        [Fact]
        public async Task ConfigureOnDemandPayoutAsync_OverlapExecutions()
        {
            var web3 = new Mock<IWeb3>();
            InitMockHandler(new MockRpcClient(), web3);

            ethereumPayoutHandler.ConfigureOnDemandPayoutAsync(CancellationToken.None);
            messageBus.NotifyNetworkBlock(poolConfig.Id, 180000000000, 10000000, poolConfig.Template);
            messageBus.NotifyNetworkBlock(poolConfig.Id, 180000000000, 10000000, poolConfig.Template);

            await Task.Delay(5000);
            web3.Verify(w => w.Eth.TransactionManager.TransactionReceiptService.PollForReceiptAsync(
                It.IsAny<string>(), It.IsAny<CancellationTokenSource>()), Times.Exactly(2));
            web3.Verify(w => w.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
            web3.Verify(w => w.Eth.Transactions.GetTransactionByHash.SendRequestAsync(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
        }

        [Fact]
        public async Task ConfigureOnDemandPayoutAsync_TransactionTimeout()
        {
            var web3 = new Mock<IWeb3>();
            web3.Setup(w => w.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.FromResult((TransactionReceipt) null));
            web3.Setup(w => w.Eth.Transactions.GetTransactionByHash.SendRequestAsync(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.FromResult((Transaction) null));
            web3.Setup(w => w.Eth.GetEtherTransferService().TransferEtherAsync(It.IsAny<string>(), It.IsAny<decimal>(), null, null, null))
                .Returns(Task.FromResult("0x444172bef57ad978655171a8af2cfd89baa02a97fcb773067aef7794d6913374"));
            web3.Setup(w => w.Eth.TransactionManager.TransactionReceiptService.PollForReceiptAsync(It.IsAny<string>(), It.IsAny<CancellationTokenSource>()))
                .Throws(new OperationCanceledException());
            InitMockHandler(new MockRpcClient(), web3);

            ethereumPayoutHandler.ConfigureOnDemandPayoutAsync(CancellationToken.None);
            // Adds txn to pending list
            messageBus.NotifyNetworkBlock(poolConfig.Id, 180000000000, 10000000, poolConfig.Template);
            await Task.Delay(5000);

            web3.Setup(w => w.Eth.TransactionManager.TransactionReceiptService.PollForReceiptAsync(It.IsAny<string>(), It.IsAny<CancellationTokenSource>()))
                .Returns(Task.FromResult(JsonConvert.DeserializeObject<TransactionReceipt>(@"{
                        ""blockHash"": ""0x67c0303244ae4beeec329e0c66198e8db8938a94d15a366c7514626528abfc8c"",
                        ""blockNumber"": ""0x6914b0"",
                        ""contractAddress"": null,
                        ""from"": ""0xc931d93e97ab07fe42d923478ba2465f2"",
                        ""to"": ""0x471a8bf3fd0dfbe20658a97155388cec674190bf"",
                        ""cumulativeGasUsed"": ""0x158e33"",
                        ""effectiveGasPrice"": ""0x230ea2a8af"",
                        ""gasUsed"": ""0xba2e6"",
                        ""logs"": [],
                        ""logsBloom"": ""0x00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"",
                        ""status"": ""0x1"",
                        ""transactionHash"": ""0x444172bef57ad978655171a8af2cfd89baa02a97fcb773067aef7794d6913374"",
                        ""transactionIndex"": ""0x4""
                    }"))).Verifiable();
            InitMockHandler(new MockRpcClient(), web3);
            // Retry txn using previous nonce
            messageBus.NotifyNetworkBlock(poolConfig.Id, 180000000000, 10000000, poolConfig.Template);
            await Task.Delay(5000);

            web3.Verify(w => w.Eth.TransactionManager.TransactionReceiptService.PollForReceiptAsync(
                It.IsAny<string>(), It.IsAny<CancellationTokenSource>()), Times.Exactly(4));
            web3.Verify(w => w.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(It.IsAny<string>(), It.IsAny<object>()), Times.Exactly(2));
            web3.Verify(w => w.Eth.Transactions.GetTransactionByHash.SendRequestAsync(It.IsAny<string>(), It.IsAny<object>()), Times.Exactly(2));
        }

        [Fact]
        public async Task ConfigureOnDemandPayoutAsync_TransactionSucceedNextCycle()
        {
            var web3 = new Mock<IWeb3>();
            web3.Setup(w => w.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.FromResult(JsonConvert.DeserializeObject<TransactionReceipt>(@"{
                        ""blockHash"": ""0x67c0303244ae4beeec329e0c66198e8db8938a94d15a366c7514626528abfc8c"",
                        ""blockNumber"": ""0x6914b0"",
                        ""contractAddress"": null,
                        ""from"": ""0xc931d93e97ab07fe42d923478ba2465f2"",
                        ""to"": ""0x471a8bf3fd0dfbe20658a97155388cec674190bf"",
                        ""cumulativeGasUsed"": ""0x158e33"",
                        ""effectiveGasPrice"": ""0x230ea2a8af"",
                        ""gasUsed"": ""0xba2e6"",
                        ""logs"": [],
                        ""logsBloom"": ""0x00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"",
                        ""status"": ""0x1"",
                        ""transactionHash"": ""0x444172bef57ad978655171a8af2cfd89baa02a97fcb773067aef7794d6913374"",
                        ""transactionIndex"": ""0x4""
                    }")));
            web3.Setup(w => w.Eth.Transactions.GetTransactionByHash.SendRequestAsync(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.FromResult((Transaction) null));
            web3.Setup(w => w.Eth.GetEtherTransferService().TransferEtherAsync(It.IsAny<string>(), It.IsAny<decimal>(), null, null, null))
                .Returns(Task.FromResult("0x444172bef57ad978655171a8af2cfd89baa02a97fcb773067aef7794d6913374"));
            web3.Setup(w => w.Eth.TransactionManager.TransactionReceiptService.PollForReceiptAsync(It.IsAny<string>(), It.IsAny<CancellationTokenSource>()))
                .Throws(new OperationCanceledException());
            InitMockHandler(new MockRpcClient(), web3);

            ethereumPayoutHandler.ConfigureOnDemandPayoutAsync(CancellationToken.None);
            // Adds txn to pending list
            messageBus.NotifyNetworkBlock(poolConfig.Id, 180000000000, 10000000, poolConfig.Template);
            await Task.Delay(5000);
            
            InitMockHandler(new MockRpcClient(), web3);
            // Retry txn using previous nonce
            messageBus.NotifyNetworkBlock(poolConfig.Id, 180000000000, 10000000, poolConfig.Template);
            await Task.Delay(5000);

            web3.Verify(w => w.Eth.TransactionManager.TransactionReceiptService.PollForReceiptAsync(
                It.IsAny<string>(), It.IsAny<CancellationTokenSource>()), Times.Exactly(2));
            web3.Verify(w => w.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(It.IsAny<string>(), It.IsAny<object>()), Times.Exactly(2));
            web3.Verify(w => w.Eth.Transactions.GetTransactionByHash.SendRequestAsync(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
        }

        #endregion

        #region GetMinimumPayout

        [Fact]
        public void GetMinimumPayout_GasDeductionPercentage_IsZero_UseDefaultMinimumPayout()
        {
            decimal actualMinimumPayout = EthereumPayoutHandler.GetMinimumPayout(DefaultMinimumPayout, 120000000000, 0, TransferGas);
            Assert.Equal(DefaultMinimumPayout, actualMinimumPayout);
        }

        [Fact]
        public void GetMinimumPayout_GasDeductionPercentage_Is_FiftyPercent_MinimumPayout_Equals_TransactionCost()
        {
            // 50% = 1 to 1 ratio
            GetMinimumPayout_GasDeductionPercentage_CheckRatio(120000000000, 50, 1);

            // This ratio should hold true regardless of gas price
            GetMinimumPayout_GasDeductionPercentage_CheckRatio(113000000000, 50, 1);
        }

        [Fact]
        public void GetMinimumPayout_GasDeductionPercentage_Is_TwentyPercent_MinimumPayout_Equals_4X_TransactionCost()
        {
            // 20% = 4 to 1 ratio
            GetMinimumPayout_GasDeductionPercentage_CheckRatio(120000000000, 20, 4);

            // This ratio should hold true regardless of gas price
            GetMinimumPayout_GasDeductionPercentage_CheckRatio(113000000000, 20, 4);
        }

        [Fact]
        public void GetMinimumPayout_GasDeductionPercentage_Is_TwentyFivePercent_MinimumPayout_Equals_3X_TransactionCost()
        {
            // 25% = 3 to 1 ratio
            GetMinimumPayout_GasDeductionPercentage_CheckRatio(120000000000, 25, 3);

            // This ratio should hold true regardless of gas price
            GetMinimumPayout_GasDeductionPercentage_CheckRatio(113000000000, 25, 3);
        }

        [Fact]
        public void GetMinimumPayout_GasDeductionPercentage_Is_FourtyPercent_MinimumPayout_Equals_1_5X_TransactionCost()
        {
            // 40% = 1.5 to 1 ratio
            GetMinimumPayout_GasDeductionPercentage_CheckRatio(120000000000, 40, (decimal) 1.5);

            // This ratio should hold true regardless of gas price
            GetMinimumPayout_GasDeductionPercentage_CheckRatio(113000000000, 40, (decimal) 1.5);
        }

        #endregion

        #region Private Methods

        private void GetMinimumPayout_GasDeductionPercentage_CheckRatio(ulong baseFeePerGas, decimal gasDeductionPercentage, decimal multiplier)
        {
            var actualMinimumPayout = EthereumPayoutHandler.GetMinimumPayout(DefaultMinimumPayout, baseFeePerGas, gasDeductionPercentage, TransferGas);
            var expectedTransactionCost = TransferGas * (baseFeePerGas / EthereumConstants.Wei);

            Assert.Equal(multiplier * expectedTransactionCost, actualMinimumPayout);
        }

        private void InitMockHandler(IRpcClient rpcClient, Mock<IWeb3> web3)
        {
            if(web3.Setups.Count == 0)
            {
                web3.Setup(w => w.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(It.IsAny<string>(), It.IsAny<object>()))
                    .Returns(Task.FromResult((TransactionReceipt) null));
                web3.Setup(w => w.Eth.Transactions.GetTransactionByHash.SendRequestAsync(It.IsAny<string>(), It.IsAny<object>()))
                    .Returns(Task.FromResult((Transaction) null));
                web3.Setup(w => w.Eth.GetEtherTransferService().TransferEtherAsync(It.IsAny<string>(), It.IsAny<decimal>(), null, null, null))
                    .Returns(Task.FromResult("0x444172bef57ad978655171a8af2cfd89baa02a97fcb773067aef7794d6913374"));
                web3.Setup(w => w.Eth.TransactionManager.TransactionReceiptService.PollForReceiptAsync(It.IsAny<string>(), It.IsAny<CancellationTokenSource>()))
                    .Returns(Task.FromResult(JsonConvert.DeserializeObject<TransactionReceipt>(@"{
                        ""blockHash"": ""0x67c0303244ae4beeec329e0c66198e8db8938a94d15a366c7514626528abfc8c"",
                        ""blockNumber"": ""0x6914b0"",
                        ""contractAddress"": null,
                        ""from"": ""0xc931d93e97ab07fe42d923478ba2465f2"",
                        ""to"": ""0x471a8bf3fd0dfbe20658a97155388cec674190bf"",
                        ""cumulativeGasUsed"": ""0x158e33"",
                        ""effectiveGasPrice"": ""0x230ea2a8af"",
                        ""gasUsed"": ""0xba2e6"",
                        ""logs"": [],
                        ""logsBloom"": ""0x00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"",
                        ""status"": ""0x1"",
                        ""transactionHash"": ""0x444172bef57ad978655171a8af2cfd89baa02a97fcb773067aef7794d6913374"",
                        ""transactionIndex"": ""0x4""
                    }"))).Verifiable();
            }

            ethereumPayoutHandler.ConfigureAsync(clusterConfig, poolConfig, rpcClient, web3.Object);
        }

        #endregion
    }
}
