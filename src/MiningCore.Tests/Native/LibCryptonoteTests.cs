using System;
using System.Linq;
using System.Text;
using MiningCore.Extensions;
using MiningCore.Native;
using Xunit;

namespace MiningCore.Tests.Native
{
	public class HashinLibCryptonoteTestsgTests : TestBase
	{
		private static readonly byte[] testValue = Enumerable.Repeat((byte)0x80, 128).ToArray();

		[Fact]
		public void Cryptonote_SlowHash_Should_Match()
		{
			var result = LibCryptonote.CryptonightHashSlow(testValue).ToHexString();

			Assert.Equal("9a267e32aefcc40ab12a906fd3f2de45a24a5ccde9e8b84528e656577f14e0fe", result);
		}

		[Fact]
		public void Cryptonote_SlowHash_Should_Throw_On_Null_Argument()
		{
			Assert.Throws<ArgumentNullException>(() => LibCryptonote.CryptonightHashSlow(null));
		}

		[Fact]
		public void Cryptonote_FastHash_Should_Match()
		{
			var result = LibCryptonote.CryptonightHashFast(testValue).ToHexString();

			Assert.Equal("80ad002b1c333a29913d9edb5340412b121c0e9045e59fa9b2aabb53f9dcc92d", result);
		}

		[Fact]
		public void Cryptonote_FastHash_Should_Throw_On_Null_Argument()
		{
			Assert.Throws<ArgumentNullException>(() => LibCryptonote.CryptonightHashFast(null));
		}

		[Fact]
		public void Cryptonote_DecodeAddress_Should_Match()
		{
			var result = LibCryptonote.DecodeAddress("9wviCeWe2D8XS82k2ovp5EUYLzBt9pYNW2LXUFsZiv8S3Mt21FZ5qQaAroko1enzw3eGr9qC7X1D7Geoo2RrAotYPwq9Gm8");
			Assert.Equal(53u, result);
		}

		[Fact]
		public void Cryptonote_DecodeAddress_Should_Throw_On_Null_Or_Empty_Argument()
		{
			Assert.Throws<ArgumentException>(() => LibCryptonote.DecodeAddress(null));
			Assert.Throws<ArgumentException>(() => LibCryptonote.DecodeAddress(""));
		}

		[Fact]
		public void Cryptonote_ConvertBlob_Should_Match()
		{
			var blob = "0105bfdfcecd05583cf50bcc743d04d831d2b119dc94ad88679e359076ee3f18d258ee138b3b420000000001d90101ff9d0106d6d6a88702020a79e36c5f5ac69abb68daa616b70e4dc911ed2edf50133fc121447cc403cd6780b4c4c32102b3adc5521c68a35e2dd1934e30b5fada872b384dbbf8c4e8130e43bd0097b8b680c0fc82aa0202b186f6745517ec23a87df7811849d71914a222c937da3e3a39c7bde6f27d2dc98090cad2c60e02df3a6eed49d05b0163986888ebe7da3fae808a72f3beec97346e0a18a960a7b180e08d84ddcb0102f37220a0c601e2dfe78cfab584cabeecf59079b3b2ee045561fb83ebf67941ba80c0caf384a30202b5e50c62333f3237d497eac37b26bd1217b6996eeb7d45e099b71b0f0b5399162b011c2515730ca7e8bb9b79e177557a1fa8b41e9aee544b25d69dc46f12f66b13f102080000000001ff0d7500".HexToByteArray();
			var result = LibCryptonote.ConvertBlob(blob).ToHexString();
			var expected = "0105bfdfcecd05583cf50bcc743d04d831d2b119dc94ad88679e359076ee3f18d258ee138b3b4200000000f262fa431f692fa1d8a6e89fb809487a2133dd6fd999d95c664b964df354ac4701";
			Assert.Equal(expected, result);
		}

		[Fact]
		public void Cryptonote_ConvertBlob_Should_Throw_On_Null_Argument()
		{
			Assert.Throws<ArgumentNullException>(() => LibCryptonote.ConvertBlob(null));
		}
	}
}
