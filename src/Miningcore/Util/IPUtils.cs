﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Miningcore.Util
{
    public class IPUtils
    {
        public static readonly IPAddress IPv4LoopBackOnIPv6 = IPAddress.Parse("::ffff:127.0.0.1");
    }
}
