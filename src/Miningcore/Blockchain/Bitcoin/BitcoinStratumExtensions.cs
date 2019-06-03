/* 
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and 
associated documentation files (the "Software"), to deal in the Software without restriction, 
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, 
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, 
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial 
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT 
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE 
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

namespace Miningcore.Blockchain.Bitcoin
{
    /// <summary>
    /// The client uses the message to advertise its features and to request/allow some protocol extensions.
    /// https://github.com/slushpool/stratumprotocol/blob/master/stratum-extensions.mediawiki#Request_miningconfigure
    /// </summary>
    public class BitcoinStratumExtensions
    {
        public const string VersionRolling = "version-rolling";
        public const string MinimumDiff = "minimum-difficulty";
        public const string SubscribeExtranonce = "subscribe-extranonce";

        public const string VersionRollingMask = VersionRolling + "." + "mask";
        public const string VersionRollingBits = VersionRolling + "." + "min-bit-count";

        public const string MinimumDiffValue = MinimumDiff + "." + "value";
    }
}
