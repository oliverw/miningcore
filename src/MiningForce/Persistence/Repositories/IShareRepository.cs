using MiningForce.Blockchain;

namespace MiningForce.Persistence.Repositories
{
    public interface IShareRepository
	{
		void PutShare(IShare share);
	}
}
