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
			CreateMap<IShare, Persistence.Model.Share>()
				.ForMember(dest => dest.Created, opt => opt.Ignore());

	        CreateMap<IShare, Persistence.Model.Block>()
		        .ForMember(dest => dest.Created, opt => opt.Ignore())
		        .ForMember(dest => dest.Status, opt => opt.Ignore());

	        // incoming mappings
        }
	}
}
