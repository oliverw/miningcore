using System;
using AutoMapper;
using CodeContracts;
using MiningForce.Configuration;
using MiningForce.Persistence;
using MiningForce.Persistence.Repositories;
using NLog;

namespace MiningForce.Payments
{
    public abstract class PayoutHandlerBase
    {
	    protected PayoutHandlerBase(IConnectionFactory cf, IMapper mapper,
		    IShareRepository shares, IBlockRepository blocks)
	    {
		    Contract.RequiresNonNull(cf, nameof(cf));
		    Contract.RequiresNonNull(mapper, nameof(mapper));
		    Contract.RequiresNonNull(shares, nameof(shares));
		    Contract.RequiresNonNull(blocks, nameof(blocks));
 
			this.cf = cf;
		    this.mapper = mapper;

		    this.shares = shares;
		    this.blocks = blocks;
	    }

	    protected ILogger logger;
	    protected readonly IConnectionFactory cf;
	    protected readonly IMapper mapper;
	    protected readonly IShareRepository shares;
	    protected readonly IBlockRepository blocks;
    }
}
