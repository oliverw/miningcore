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
		    IShareRepository shareRepo, IBlockRepository blockRepo)
	    {
		    Contract.RequiresNonNull(cf, nameof(cf));
		    Contract.RequiresNonNull(mapper, nameof(mapper));
		    Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
		    Contract.RequiresNonNull(blockRepo, nameof(blockRepo));
 
			this.cf = cf;
		    this.mapper = mapper;
		    this.shareRepo = shareRepo;
		    this.blockRepo = blockRepo;
	    }

	    protected ILogger logger;
	    protected readonly IConnectionFactory cf;
	    protected readonly IMapper mapper;
	    protected readonly IShareRepository shareRepo;
	    protected readonly IBlockRepository blockRepo;
    }
}
