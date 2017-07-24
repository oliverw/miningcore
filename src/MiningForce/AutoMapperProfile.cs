using System;
using AutoMapper;
using MiningForce.Blockchain;

namespace MiningForce
{
    public class AutoMapperProfile : Profile
    {
        public AutoMapperProfile()
        {
			// outgoing mappings
			CreateMap<IShare, Persistence.Model.Share>();

	        CreateMap<IShare, Persistence.Model.Block>()
		        .ForMember(dest => dest.Status, opt => opt.Ignore());

	        CreateMap<Persistence.Model.BlockStatus, string>().ConvertUsing(e => e.ToString().ToLower());

			// incoming mappings
		}
	}
}
