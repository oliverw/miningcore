using Microsoft.Azure.Cosmos;
using Miningcore.Persistence.Cosmos.Entities;
using Miningcore.Extensions;
using NLog;
using Miningcore.Persistence.Repositories;
using Miningcore.Configuration;

namespace Miningcore.Persistence.Cosmos.Repositories
{
    public class BalanceChangeRepository: IBalanceChangeRepository
    {
        public BalanceChangeRepository(CosmosClient cosmosClient, ClusterConfig clusterConfig)
        {
            this.cosmosClient = cosmosClient;
            this.databaseId = clusterConfig.Persistence.Cosmos.DatabaseId;
        }

        private readonly CosmosClient cosmosClient;
        private readonly string databaseId;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        public async Task AddNewBalanceChange(string poolId, string address, decimal amount, string usage)
        {
            logger.LogInvoke();

            var date = DateTime.UtcNow.Date;

            var balanceChange = new BalanceChange()
            {
                PoolId = poolId,
                Address = address,
                Amount = amount,
                Usage = usage,
                Created = date
            };
            var requestOptions = new ItemRequestOptions();

            if (!String.IsNullOrEmpty(balanceChange.ETag))
            {
                requestOptions.IfMatchEtag = balanceChange.ETag;
            }
            await cosmosClient.GetContainer(databaseId, balanceChange.CollectionName)
                .CreateItemAsync(balanceChange, new PartitionKey(balanceChange.PartitionKey), requestOptions);
        }

        public async Task<BalanceChange> GetBalanceChangeByDate(string poolId, string address, DateTime created)
        {
            logger.LogInvoke();

            var date = created.Date;
            var balanceChange = new BalanceChange()
            {
                PoolId = poolId,
                Address = address,
                Created = date
            };
            try
            {
                ItemResponse<BalanceChange> balanceChangeResponse = await cosmosClient.GetContainer(databaseId, balanceChange.CollectionName)
                    .ReadItemAsync<BalanceChange>(balanceChange.Id, new PartitionKey(balanceChange.PartitionKey));
                return balanceChangeResponse.Resource;
            }
            catch(CosmosException ex) when(ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task UpdateBalanceChange(BalanceChange balanceChange)
        {
            logger.LogInvoke();

            await cosmosClient.GetContainer(databaseId, balanceChange.CollectionName)
                .ReplaceItemAsync(balanceChange, balanceChange.Id, new PartitionKey(balanceChange.PartitionKey));
        }
    }
}
