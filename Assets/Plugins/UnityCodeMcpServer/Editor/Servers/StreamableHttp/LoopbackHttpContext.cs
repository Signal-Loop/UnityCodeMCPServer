using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace UnityCodeMcpServer.Servers.StreamableHttp
{
    public sealed class LoopbackHttpRequest
    {
        private readonly Dictionary<string, string> _headers;

        public LoopbackHttpRequest(
            string httpMethod,
            string pathAndQuery,
            IDictionary<string, string> headers,
            Stream inputStream,
            EndPoint remoteEndPoint = null)
        {
            HttpMethod = httpMethod ?? throw new ArgumentNullException(nameof(httpMethod));
            PathAndQuery = pathAndQuery ?? throw new ArgumentNullException(nameof(pathAndQuery));
            InputStream = inputStream ?? throw new ArgumentNullException(nameof(inputStream));
            RemoteEndPoint = remoteEndPoint;
            _headers = headers != null
                ? new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string HttpMethod { get; }

        public string PathAndQuery { get; }

        public Stream InputStream { get; }

        public EndPoint RemoteEndPoint { get; }

        public IReadOnlyDictionary<string, string> Headers => _headers;

        public string GetHeader(string name)
        {
            return name != null && _headers.TryGetValue(name, out string value) ? value : null;
        }
    }

    public sealed class LoopbackHttpResponse
    {
        public LoopbackHttpResponse(Stream outputStream)
        {
            OutputStream = outputStream ?? throw new ArgumentNullException(nameof(outputStream));
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public int StatusCode { get; set; }

        public string ContentType { get; set; }

        public long? ContentLength64 { get; set; }

        public Stream OutputStream { get; }

        public IDictionary<string, string> Headers { get; }

        public bool IsClosed { get; private set; }

        public void Close()
        {
            OutputStream.Flush();
            IsClosed = true;
        }
    }

    public sealed class LoopbackHttpContext
    {
        public LoopbackHttpContext(LoopbackHttpRequest request, LoopbackHttpResponse response)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Response = response ?? throw new ArgumentNullException(nameof(response));
        }

        public LoopbackHttpRequest Request { get; }

        public LoopbackHttpResponse Response { get; }
    }
}
