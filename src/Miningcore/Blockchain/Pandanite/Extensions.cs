using System;
using System.Text;

namespace Miningcore.Blockchain.Pandanite 
{
    public static class Extensions 
    {
        public static string AsString(this byte[] hash)
        {
            var builder = new StringBuilder();

            for (int i = 0; i < hash.Length; i++)
            {
                builder.Append(hash[i].ToString("x2"));
            }

            return builder.ToString().ToUpper();
        }

        public static byte[] ToByteArray(this string str)
        {
            var bytes = new List<byte>();
            for(int i = 0; i < str.Length; i +=2)
            {
                var a = Convert.ToInt64(str.Substring(i, 2), 16);
                var b = Convert.ToChar(a);
                bytes.Add(Convert.ToByte(b));
            }

            return bytes.ToArray();
        }
    }
}
