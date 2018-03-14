/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using AutoMapper;
using MiningCore.Blockchain;
using MiningCore.Configuration;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Model.Projections;
using MinerStats = MiningCore.Persistence.Model.Projections.MinerStats;

namespace MiningCore
{
    public class AutoMapperProfile : Profile
    {
        public AutoMapperProfile()
        {
            //////////////////////
            // outgoing mappings

            CreateMap<Blockchain.Share, Persistence.Model.Share>();

            CreateMap<Blockchain.Share, Block>()
                .ForMember(dest => dest.Reward, opt => opt.MapFrom(src => src.BlockReward))
                .ForMember(dest => dest.Hash, opt => opt.MapFrom(src => src.BlockHash))
                .ForMember(dest => dest.Status, opt => opt.Ignore());

            CreateMap<BlockStatus, string>().ConvertUsing(e => e.ToString().ToLower());

            CreateMap<Mining.PoolStats, PoolStats>()
                .ForMember(dest => dest.PoolId, opt => opt.Ignore())
                .ForMember(dest => dest.Created, opt => opt.Ignore());

            CreateMap<BlockchainStats, PoolStats>()
                .ForMember(dest => dest.PoolId, opt => opt.Ignore())
                .ForMember(dest => dest.Created, opt => opt.Ignore());

            // API
            CreateMap<PoolConfig, Api.Responses.PoolInfo>();
            CreateMap<PoolStats, Api.Responses.PoolInfo>();
            CreateMap<PoolStats, Api.Responses.AggregatedPoolStats>();
            CreateMap<Block, Api.Responses.Block>();
            CreateMap<Payment, Api.Responses.Payment>();
            CreateMap<BalanceChange, Api.Responses.BalanceChange>();
            CreateMap<CoinConfig, Api.Responses.ApiCoinConfig>();
            CreateMap<PoolPaymentProcessingConfig, Api.Responses.ApiPoolPaymentProcessingConfig>();

            CreateMap<MinerStats, Api.Responses.MinerStats>()
                .ForMember(dest => dest.LastPayment, opt => opt.Ignore())
                .ForMember(dest => dest.LastPaymentLink, opt => opt.Ignore());

            CreateMap<WorkerPerformanceStats, Api.Responses.WorkerPerformanceStats>();
            CreateMap<WorkerPerformanceStatsContainer, Api.Responses.WorkerPerformanceStatsContainer>();

            // PostgreSQL
            CreateMap<Persistence.Model.Share, Persistence.Postgres.Entities.Share>();
            CreateMap<Block, Persistence.Postgres.Entities.Block>();
            CreateMap<Balance, Persistence.Postgres.Entities.Balance>();
            CreateMap<Payment, Persistence.Postgres.Entities.Payment>();
            CreateMap<PoolStats, Persistence.Postgres.Entities.PoolStats>();

            CreateMap<MinerWorkerPerformanceStats, Persistence.Postgres.Entities.MinerWorkerPerformanceStats>()
                .ForMember(dest => dest.Id, opt => opt.Ignore());

            //////////////////////
            // incoming mappings

            // PostgreSQL
            CreateMap<Persistence.Postgres.Entities.Share, Persistence.Model.Share>();
            CreateMap<Persistence.Postgres.Entities.Block, Block>();
            CreateMap<Persistence.Postgres.Entities.Balance, Balance>();
            CreateMap<Persistence.Postgres.Entities.Payment, Payment>();
            CreateMap<Persistence.Postgres.Entities.BalanceChange, BalanceChange>();
            CreateMap<Persistence.Postgres.Entities.PoolStats, PoolStats>();
            CreateMap<Persistence.Postgres.Entities.MinerWorkerPerformanceStats, MinerWorkerPerformanceStats>();
            CreateMap<Persistence.Postgres.Entities.MinerWorkerPerformanceStats, Api.Responses.MinerPerformanceStats>();

            CreateMap<PoolStats, Mining.PoolStats>();
            CreateMap<BlockchainStats, Mining.PoolStats>();

            CreateMap<PoolStats, BlockchainStats>()
                .ForMember(dest => dest.RewardType, opt => opt.Ignore())
                .ForMember(dest => dest.NetworkType, opt => opt.Ignore());
        }
    }
}
