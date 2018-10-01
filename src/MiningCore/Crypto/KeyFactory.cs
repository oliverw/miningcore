using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MiningCore.Crypto
{
    public static class KeyFactory
    {
        const int PasswordIterations = 5000;
        private static readonly byte[] NoSalt = Enumerable.Repeat((byte) 0, 32).ToArray();

        public static byte[] Derive256BitKey(string password)
        {
            using (var kbd = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(password), NoSalt, PasswordIterations))
            {
                var block = kbd.GetBytes(32);
                return block;
            }
        }
    }
}
