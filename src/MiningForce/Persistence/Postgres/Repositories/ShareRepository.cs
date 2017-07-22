using System;
using Dapper;
using MiningForce.Blockchain;
using MiningForce.Persistence.Repositories;
using Newtonsoft.Json;
using NLog;

namespace MiningForce.Persistence.Postgres.Repositories
{
    public class ShareRepository : IShareRepository
    {
	    public ShareRepository(ConnectionFactory connectionFactory)
	    {
		    this.connectionFactory = connectionFactory;
	    }

	    private readonly ConnectionFactory connectionFactory;
	    private readonly ILogger logger = LogManager.GetCurrentClassLogger();

		public void PutShare(IShare share)
	    {
			logger.Trace(()=> "PutShare");

		    using (var con = connectionFactory.GetConnection())
		    {
			    using (var tx = con.BeginTransaction())
			    {
				    try
				    {
					    con.Execute("INSERT INTO shares(coin, blockheight, difficulty, worker, ipaddress) " +
									"VALUES(@coin, @blockheight, @difficulty, @worker, @ipaddress)", share, tx);

					    if (share.IsBlockCandidate)
					    {
						    var block = new Model.Block
						    {
							    Coin = share.Coin.ToString(),
							    Blockheight = share.BlockHeight,
							    Status = Model.Block.StatusPending,
							    TransactionConfirmationData = share.TransactionConfirmationData
						    };

						    con.Execute("INSERT INTO blocks(coin, blockheight, status, transactionconfirmationdata) " +
										"VALUES(@coin, @blockheight, @status, @transactionconfirmationdata)", block, tx);
					    }

						tx.Commit();
				    }

					catch (Exception)
				    {
					    tx.Rollback();
					    throw;
				    }
			    }
		    }
	    }
	}
}
