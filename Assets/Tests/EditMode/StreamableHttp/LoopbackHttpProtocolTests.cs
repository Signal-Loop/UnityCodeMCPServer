using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;
using UnityCodeMcpServer.Servers.StreamableHttp;

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
    }
}
