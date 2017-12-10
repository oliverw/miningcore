using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MiningCore.Buffers;
using MiningCore.Extensions;
using MiningCore.Util;
using NLog;
using Xunit;

namespace MiningCore.Tests.Util
{
    public class PooledLineBufferTests : TestBase
    {
        private byte[] GetBuffer(string str)
        {
            return Encoding.UTF8.GetBytes(str);
        }

        private string GetString(PooledArraySegment<byte> seg)
        {
            return Encoding.UTF8.GetString(seg.Array, seg.Offset, seg.Size);
        }

        [Fact]
        public void PooledLineBuffer_Partial_Line()
        {
            var plb = new PooledLineBuffer(LogManager.CreateNullLogger());
            var recvCount = 0;
            var errCount = 0;

            var buf = GetBuffer("abc");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x)=> recvCount++, (ex) => errCount++);

            Assert.Equal(recvCount, 0);
            Assert.Equal(errCount, 0);
        }

        [Fact]
        public void PooledLineBuffer_Partial_Line_Double()
        {
            var plb = new PooledLineBuffer(LogManager.CreateNullLogger());
            var recvCount = 0;
            var errCount = 0;

            var buf = GetBuffer("abc");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) => recvCount++, (ex) => errCount++);

            buf = GetBuffer("def");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) => recvCount++, (ex) => errCount++);

            Assert.Equal(recvCount, 0);
            Assert.Equal(errCount, 0);
        }

        [Fact]
        public void PooledLineBuffer_Partial_Line_Double_With_NewLine()
        {
            var plb = new PooledLineBuffer(LogManager.CreateNullLogger());
            var recvCount = 0;
            var errCount = 0;
            var result = string.Empty;

            var buf = GetBuffer("abc");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) => recvCount++, (ex) => errCount++);

            buf = GetBuffer("def\n");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    recvCount++;
                    result = GetString(x);
                }, (ex) => errCount++);

            Assert.Equal(recvCount, 1);
            Assert.Equal(result, "abcdef");
            Assert.Equal(errCount, 0);
        }

        [Fact]
        public void PooledLineBuffer_Partial_Line_Double_With_NewLine_With_Leading_NewLines()
        {
            var plb = new PooledLineBuffer(LogManager.CreateNullLogger());
            var recvCount = 0;
            var errCount = 0;
            var result = string.Empty;

            var buf = GetBuffer("\n\nabc");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) => recvCount++, (ex) => errCount++);

            buf = GetBuffer("def\n");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    recvCount++;
                    result = GetString(x);
                }, (ex) => errCount++);

            Assert.Equal(recvCount, 1);
            Assert.Equal(result, "abcdef");
            Assert.Equal(errCount, 0);
        }

        [Fact]
        public void PooledLineBuffer_Partial_Line_Double_With_NewLine_With_Trailing_NewLines()
        {
            var plb = new PooledLineBuffer(LogManager.CreateNullLogger());
            var recvCount = 0;
            var errCount = 0;
            var result = string.Empty;

            var buf = GetBuffer("abc");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) => recvCount++, (ex) => errCount++);

            buf = GetBuffer("def\n\n\n");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    recvCount++;
                    result = GetString(x);
                }, (ex) => errCount++);

            Assert.Equal(recvCount, 1);
            Assert.Equal(result, "abcdef");
            Assert.Equal(errCount, 0);
        }

        [Fact]
        public void PooledLineBuffer_Partial_Dont_Emit_Empty_Lines()
        {
            var plb = new PooledLineBuffer(LogManager.CreateNullLogger());
            var recvCount = 0;
            var errCount = 0;

            var buf = GetBuffer("\n\n\n");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    recvCount++;
                }, (ex) => errCount++);

            Assert.Equal(recvCount, 0);
            Assert.Equal(errCount, 0);
        }

        [Fact]
        public void PooledLineBuffer_Partial_Enforce_Limits()
        {
            var plb = new PooledLineBuffer(LogManager.CreateNullLogger(), 3);
            var recvCount = 0;
            var errCount = 0;

            var buf = GetBuffer("abcd");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    recvCount++;
                }, (ex) => errCount++);

            Assert.Equal(recvCount, 0);
            Assert.Equal(errCount, 1);
        }

        [Fact]
        public void PooledLineBuffer_Partial_Enforce_Limits_Queued()
        {
            var plb = new PooledLineBuffer(LogManager.CreateNullLogger(), 5);
            var recvCount = 0;
            var errCount = 0;

            var buf = GetBuffer("abcd");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    recvCount++;
                }, (ex) => errCount++);

            Assert.Equal(recvCount, 0);
            Assert.Equal(errCount, 0);

            buf = GetBuffer("def");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    recvCount++;
                }, (ex) => errCount++);

            Assert.Equal(recvCount, 0);
            Assert.Equal(errCount, 1);
        }

        [Fact]
        public void PooledLineBuffer_Single_Line()
        {
            var plb = new PooledLineBuffer(LogManager.CreateNullLogger());
            var recvCount = 0;
            var errCount = 0;
            var result = string.Empty;

            var buf = GetBuffer("abc\n");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    recvCount++;
                    result = GetString(x);
                }, (ex) => errCount++);

            Assert.Equal(recvCount, 1);
            Assert.Equal(result, "abc");
            Assert.Equal(errCount, 0);
        }

        [Fact]
        public void PooledLineBuffer_Multi_Line_Batch()
        {
            var plb = new PooledLineBuffer(LogManager.CreateNullLogger());
            var errCount = 0;
            var results = new List<string>();

            var buf = GetBuffer("abc\ndef\nghi\n");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    results.Add(GetString(x));
                }, (ex) => errCount++);

            Assert.Equal(results.Count, 3);
            Assert.Equal(results[0], "abc");
            Assert.Equal(results[1], "def");
            Assert.Equal(results[2], "ghi");
            Assert.Equal(errCount, 0);
        }

        [Fact]
        public void PooledLineBuffer_Single_Characters()
        {
            var plb = new PooledLineBuffer(LogManager.CreateNullLogger());
            var errCount = 0;
            var results = new List<string>();

            var buf = GetBuffer("a");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    results.Add(GetString(x));
                }, (ex) => errCount++);

            buf = GetBuffer("b");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    results.Add(GetString(x));
                }, (ex) => errCount++);

            buf = GetBuffer("c\n");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    results.Add(GetString(x));
                }, (ex) => errCount++);

            Assert.Equal(results.Count, 1);
            Assert.Equal(results[0], "abc");
            Assert.Equal(errCount, 0);
        }

        [Fact]
        public void PooledLineBuffer_Single_Character_Lines()
        {
            var plb = new PooledLineBuffer(LogManager.CreateNullLogger());
            var errCount = 0;
            var results = new List<string>();

            var buf = GetBuffer("a\n");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    results.Add(GetString(x));
                }, (ex) => errCount++);

            buf = GetBuffer("b\n");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    results.Add(GetString(x));
                }, (ex) => errCount++);

            buf = GetBuffer("c\n");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    results.Add(GetString(x));
                }, (ex) => errCount++);

            Assert.Equal(results.Count, 3);
            Assert.Equal(results[0], "a");
            Assert.Equal(results[1], "b");
            Assert.Equal(results[2], "c");
            Assert.Equal(errCount, 0);
        }

        [Fact]
        public void PooledLineBuffer_Combo1()
        {
            var plb = new PooledLineBuffer(LogManager.CreateNullLogger());
            var errCount = 0;
            var results = new List<string>();

            var buf = GetBuffer("abc\ndef");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    results.Add(GetString(x));
                }, (ex) => errCount++);

            Assert.Equal(results.Count, 1);
            Assert.Equal(results[0], "abc");
            Assert.Equal(errCount, 0);
        }

        [Fact]
        public void PooledLineBuffer_Combo2()
        {
            var plb = new PooledLineBuffer(LogManager.CreateNullLogger());
            var errCount = 0;
            var results = new List<string>();

            var buf = GetBuffer("abc\ndef");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    results.Add(GetString(x));
                }, (ex) => errCount++);

            buf = GetBuffer("ghi\n");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    results.Add(GetString(x));
                }, (ex) => errCount++);

            Assert.Equal(results.Count, 2);
            Assert.Equal(results[0], "abc");
            Assert.Equal(results[1], "defghi");
            Assert.Equal(errCount, 0);
        }

        [Fact]
        public void PooledLineBuffer_Combo3()
        {
            var plb = new PooledLineBuffer(LogManager.CreateNullLogger());
            var errCount = 0;
            var results = new List<string>();

            var buf = GetBuffer("abc\ndef");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    results.Add(GetString(x));
                }, (ex) => errCount++);

            buf = GetBuffer("ghi");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    results.Add(GetString(x));
                }, (ex) => errCount++);

            buf = GetBuffer("jkl\nmno\n");

            plb.Receive(buf, buf.Length,
                (src, dst, count) => Array.Copy(src, 0, dst, 0, count),
                (x) =>
                {
                    results.Add(GetString(x));
                }, (ex) => errCount++);

            Assert.Equal(results.Count, 3);
            Assert.Equal(results[0], "abc");
            Assert.Equal(results[1], "defghijkl");
            Assert.Equal(results[2], "mno");
            Assert.Equal(errCount, 0);
        }
    }
}
