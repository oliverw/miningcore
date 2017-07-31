using System;
using System.Net;

namespace MiningForce.Banning
{
	public interface IBanManager
	{
		bool IsBanned(IPAddress address);
		void Ban(IPAddress address, TimeSpan duration);
	}
}
