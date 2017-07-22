using System.Threading.Tasks;
using MiningForce.Blockchain;
using MiningForce.Persistence.Model;

namespace MiningForce.Persistence.Repositories
{
    public interface IShareRepository
	{
		void PutShare(IShare share);
	}
}
