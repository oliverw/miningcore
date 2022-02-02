using AutoMapper;
using Microsoft.Azure.Cosmos;
using Miningcore.Persistence.Cosmos.Entities;
using Miningcore.Extensions;
using NLog;
using Miningcore.Persistence.Repositories;
using Miningcore.Configuration;

namespace Miningcore.Persistence.Cosmos.Repositories
{
    public class BalanceChangeRepository : IBalanceChangeRepository
    {
        public BalanceChangeRepository(CosmosClient cosmosClient, ClusterConfig clusterConfig, IMapper mapper)
        {
            this.cosmosClient = cosmosClient;
            this.databaseId = clusterConfig.Persistence.Cosmos.DatabaseId;
            this.mapper = mapper;
        }

        private readonly CosmosClient cosmosClient;
        private readonly IMapper mapper;
        private readonly string databaseId;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        public async Task AddNewBalanceChange(string poolId, string address, decimal amount, string usage)
        {
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

            if(!String.IsNullOrEmpty(balanceChange.ETag))
            {
                requestOptions.IfMatchEtag = balanceChange.ETag;
            }
            await cosmosClient.GetContainer(databaseId, balanceChange.CollectionName)
                .CreateItemAsync(balanceChange, new PartitionKey(balanceChange.PartitionKey), requestOptions);
        }

        public async Task<Model.BalanceChange> GetBalanceChangeByDate(string poolId, string address, DateTime created)
        {
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
                return mapper.Map<Model.BalanceChange>(balanceChangeResponse.Resource);
            }
            catch(CosmosException ex) when(ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task UpdateBalanceChange(Model.BalanceChange balanceChange)
        {
            var entity = mapper.Map<BalanceChange>(balanceChange);

            await cosmosClient.GetContainer(databaseId, entity.CollectionName)
                .ReplaceItemAsync(entity, entity.Id, new PartitionKey(entity.PartitionKey));
        }

        public async Task<uint> GetBalanceChangesCountAsync(string poolId, string address = null)
        {
            throw new NotImplementedException();
        }

        public async Task<Model.BalanceChange[]> PageBalanceChangesAsync(string poolId, string address, int page, int pageSize)
        {
            throw new NotImplementedException();
        }
    }
}
