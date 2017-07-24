using AutoMapper;
using MiningForce.Configuration;
using MiningForce.Persistence;
using MiningForce.Persistence.Repositories;

namespace MiningForce.Payments
{
    public abstract class PayoutHandlerBase
    {
	    protected PayoutHandlerBase(IConnectionFactory cf, IMapper mapper,
		    IShareRepository shares, IBlockRepository blocks)
	    {
		    this.cf = cf;
		    this.mapper = mapper;

		    this.shares = shares;
		    this.blocks = blocks;
	    }

	    protected readonly IConnectionFactory cf;
	    protected readonly IMapper mapper;
	    protected readonly IShareRepository shares;
	    protected readonly IBlockRepository blocks;
    }
}
