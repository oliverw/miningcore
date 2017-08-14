using AutoMapper;
using MiningCore.Api.Responses;
using MiningCore.Blockchain;
using MiningCore.Configuration;
using MiningCore.Persistence.Model;

namespace MiningCore
{
    public class AutoMapperProfile : Profile
    {
        public AutoMapperProfile()
        {
            //////////////////////
            // outgoing mappings

            CreateMap<IShare, Share>();

            CreateMap<IShare, Block>()
                .ForMember(dest => dest.Reward, opt => opt.MapFrom(src => src.BlockReward))
                .ForMember(dest => dest.Status, opt => opt.Ignore());

            CreateMap<BlockStatus, string>().ConvertUsing(e => e.ToString().ToLower());

            // API

            CreateMap<PoolConfig, PoolInfo>();

            // PostgreSQL
            CreateMap<Share, Persistence.Postgres.Entities.Share>();
            CreateMap<Block, Persistence.Postgres.Entities.Block>();
            CreateMap<Balance, Persistence.Postgres.Entities.Balance>();
            CreateMap<Payment, Persistence.Postgres.Entities.Payment>();

            //////////////////////
            // incoming mappings

            // PostgreSQL
            CreateMap<Persistence.Postgres.Entities.Share, Share>();
            CreateMap<Persistence.Postgres.Entities.Block, Block>();
            CreateMap<Persistence.Postgres.Entities.Balance, Balance>();
            CreateMap<Persistence.Postgres.Entities.Payment, Payment>();
        }
    }
}
