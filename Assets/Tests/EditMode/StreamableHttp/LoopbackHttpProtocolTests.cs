using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityCodeMcpServer.HttpServer;

namespace UnityCodeMcpServer.Tests.EditMode.StreamableHttp
{
    [TestFixture]
    public class LoopbackHttpProtocolTests
    {
        [Test]
        public void ReadRequestAsync_ContentLengthBody_ReadsRequestBody()
        {
            const string body = "{\"method\":\"ping\"}";
            byte[] requestBytes = Encoding.ASCII.GetBytes(
                "POST /mcp/ HTTP/1.1\r\n" +
                "Host: 127.0.0.1\r\n" +
                $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n" +
                body);

            LoopbackHttpRequest request = LoopbackHttpProtocol
                .ReadRequestAsync(new MemoryStream(requestBytes), null, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(request.HttpMethod, Is.EqualTo("POST"));
            Assert.That(request.PathAndQuery, Is.EqualTo("/mcp/"));

            using StreamReader reader = new(request.InputStream, Encoding.UTF8, true, 1024, leaveOpen: true);
            Assert.That(reader.ReadToEnd(), Is.EqualTo(body));
        }

        [Test]
        public void ReadRequestAsync_ChunkedBody_ReassemblesRequestBody()
        {
            const string body = "{\"method\":\"ping\"}";
            byte[] requestBytes = Encoding.ASCII.GetBytes(
                "POST /mcp/ HTTP/1.1\r\n" +
                "Host: 127.0.0.1\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                $"{Encoding.UTF8.GetByteCount(body):X}\r\n{body}\r\n" +
                "0\r\n\r\n");

            LoopbackHttpRequest request = LoopbackHttpProtocol
                .ReadRequestAsync(new MemoryStream(requestBytes), null, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            using StreamReader reader = new(request.InputStream, Encoding.UTF8, true, 1024, leaveOpen: true);
            Assert.That(reader.ReadToEnd(), Is.EqualTo(body));
        }

        [Test]
        public void ReadRequestAsync_ConnectionClosesBeforeHeadersComplete_ReturnsNull()
        {
            LoopbackHttpRequest request = LoopbackHttpProtocol
                .ReadRequestAsync(new MemoryStream(Array.Empty<byte>()), null, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(request, Is.Null);
        }

        [Test]
        public async Task ReadRequestAsync_IncompleteHeaders_ThrowsTimeoutException()
        {
            TimeoutException exception = null;

            try
            {
                await LoopbackHttpProtocol.ReadRequestAsync(
                    new BlockingReadStream(),
                    null,
                    CancellationToken.None,
                    TimeSpan.FromMilliseconds(50),
                    TimeSpan.FromSeconds(1));
                Assert.Fail("Expected TimeoutException");
            }
            catch (TimeoutException ex)
            {
                exception = ex;
            }

            Assert.That(exception.Message, Does.Contain("header"));
        }

        [Test]
        public async Task ReadRequestAsync_IncompleteBody_ThrowsTimeoutException()
        {
            byte[] headerBytes = Encoding.ASCII.GetBytes(
                "POST /mcp/ HTTP/1.1\r\n" +
                "Host: 127.0.0.1\r\n" +
                "Content-Length: 8\r\n\r\n");

            TimeoutException exception = null;

            try
            {
                await LoopbackHttpProtocol.ReadRequestAsync(
                    new PrefixThenBlockStream(headerBytes),
                    null,
                    CancellationToken.None,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromMilliseconds(50));
                Assert.Fail("Expected TimeoutException");
            }
            catch (TimeoutException ex)
            {
                exception = ex;
            }

            Assert.That(exception.Message, Does.Contain("body"));
        }

        private class BlockingReadStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                TaskCompletionSource<int> tcs = new();
                cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
                return tcs.Task;
            }
        }

        private sealed class PrefixThenBlockStream : BlockingReadStream
        {
            private readonly byte[] _prefix;
            private int _offset;

            public PrefixThenBlockStream(byte[] prefix)
            {
                _prefix = prefix;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                if (_offset < _prefix.Length)
                {
                    int copyCount = Math.Min(count, _prefix.Length - _offset);
                    Array.Copy(_prefix, _offset, buffer, offset, copyCount);
                    _offset += copyCount;
                    return Task.FromResult(copyCount);
                }

                return base.ReadAsync(buffer, offset, count, cancellationToken);
            }
        }
    }
}
