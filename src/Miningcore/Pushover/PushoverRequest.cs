using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miningcore.Pushover
{
    public class PushoverRequest
    {
        public string Token { get; set; }
        public string User { get; set; }
        public string Device { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public int Priority { get; set; }
        public int Timestamp { get; set; }
        public string Sound { get; set; }
    }
}
