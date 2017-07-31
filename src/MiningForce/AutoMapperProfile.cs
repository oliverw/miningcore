using System;
using AutoMapper;
using MiningForce.Blockchain;

namespace MiningForce
{
    public class AutoMapperProfile : Profile
    {
        public AutoMapperProfile()
        {
			//////////////////////
			// outgoing mappings

			CreateMap<IShare, Persistence.Model.Share>();

			CreateMap<IShare, Persistence.Model.Block>()
		        .ForMember(dest => dest.Reward, opt => opt.MapFrom(src => src.BlockReward))
				.ForMember(dest => dest.Status, opt => opt.Ignore());

	        CreateMap<Persistence.Model.BlockStatus, string>().ConvertUsing(e => e.ToString().ToLower());

			// PostgreSQL
			CreateMap<Persistence.Model.Share, Persistence.Postgres.Entities.Share>();
	        CreateMap<Persistence.Model.Block, Persistence.Postgres.Entities.Block>();
	        CreateMap<Persistence.Model.Balance, Persistence.Postgres.Entities.Balance>();
	        CreateMap<Persistence.Model.Payment, Persistence.Postgres.Entities.Payment>();

			//////////////////////
			// incoming mappings

			// PostgreSQL
	        CreateMap<Persistence.Postgres.Entities.Share, Persistence.Model.Share>();
	        CreateMap<Persistence.Postgres.Entities.Block, Persistence.Model.Block>();
	        CreateMap<Persistence.Postgres.Entities.Balance, Persistence.Model.Balance>();
	        CreateMap<Persistence.Postgres.Entities.Payment, Persistence.Model.Payment>();
		}
	}
}
