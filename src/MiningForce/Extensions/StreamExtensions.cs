using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MiningForce.Extensions
{
    public static class StreamExtensions
    {
	    public static void Write(this Stream stream, byte[] data)
	    {
		    stream.Write(data, 0, data.Length);
	    }
    }
}
