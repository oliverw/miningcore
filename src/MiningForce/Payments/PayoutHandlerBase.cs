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
		    IShareRepository shareRepo, 
			IBlockRepository blockRepo, 
			IBalanceRepository balanceRepo,
		    IPaymentRepository paymentRepo)
	    {
		    Contract.RequiresNonNull(cf, nameof(cf));
		    Contract.RequiresNonNull(mapper, nameof(mapper));
		    Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
		    Contract.RequiresNonNull(blockRepo, nameof(blockRepo));
		    Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));
		    Contract.RequiresNonNull(paymentRepo, nameof(paymentRepo));

			this.cf = cf;
		    this.mapper = mapper;
		    this.shareRepo = shareRepo;
		    this.blockRepo = blockRepo;
		    this.balanceRepo = balanceRepo;
		    this.paymentRepo = paymentRepo;
	    }

		protected ILogger logger;
	    protected readonly IConnectionFactory cf;
	    protected readonly IMapper mapper;
	    protected readonly IShareRepository shareRepo;
	    protected readonly IBlockRepository blockRepo;
	    protected readonly IBalanceRepository balanceRepo;
	    protected readonly IPaymentRepository paymentRepo;

	    protected abstract string LogCategory { get; }
	}
}
