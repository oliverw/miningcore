using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.Persistence.Model
{
    public enum BlockStatus
    {
		Pending = 1,
	    Orphaned = 2,
		Confirmed = 3,
    }
}
