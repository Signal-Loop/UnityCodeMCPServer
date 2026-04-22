using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace UnityCodeMcpServer.Servers.StreamableHttp
{
    public static class LoopbackHttpProtocol
    {
        public static async UniTask<LoopbackHttpRequest> ReadRequestAsync(Stream stream, EndPoint remoteEndPoint, CancellationToken ct)
        {
            MemoryStream headerBuffer = new();
            byte[] readBuffer = new byte[4096];
            int headerEndIndex = -1;

            while (headerEndIndex < 0)
            {
                int bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length, ct);
                if (bytesRead == 0)
                {
                    return null;
                }

                headerBuffer.Write(readBuffer, 0, bytesRead);
                byte[] currentBytes = headerBuffer.GetBuffer();
                headerEndIndex = FindHeaderEnd(currentBytes, (int)headerBuffer.Length);

                if (headerBuffer.Length > 64 * 1024)
                {
                    throw new InvalidDataException("HTTP headers too large");
                }
            }

            byte[] requestBytes = headerBuffer.ToArray();
            string headerText = Encoding.ASCII.GetString(requestBytes, 0, headerEndIndex);
            string[] headerLines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (headerLines.Length == 0)
            {
                throw new InvalidDataException("Missing HTTP request line");
            }

            string[] requestLineParts = headerLines[0].Split(' ');
            if (requestLineParts.Length < 2)
            {
                throw new InvalidDataException("Invalid HTTP request line");
            }

            Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < headerLines.Length; i++)
            {
                string line = headerLines[i];
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                int separatorIndex = line.IndexOf(':');
                if (separatorIndex <= 0)
                {
                    throw new InvalidDataException("Invalid HTTP header line");
                }

                string name = line.Substring(0, separatorIndex).Trim();
                string value = line.Substring(separatorIndex + 1).Trim();
                headers[name] = value;
            }

            int bodyStartIndex = headerEndIndex + 4;
            int bufferedBodyLength = Math.Max(0, requestBytes.Length - bodyStartIndex);
            byte[] bufferedBodyBytes = new byte[bufferedBodyLength];
            if (bufferedBodyLength > 0)
            {
                Array.Copy(requestBytes, bodyStartIndex, bufferedBodyBytes, 0, bufferedBodyLength);
            }

            BufferedByteReader reader = new(stream, bufferedBodyBytes);
            byte[] bodyBytes = await ReadBodyAsync(reader, headers, ct);

            return new LoopbackHttpRequest(
                requestLineParts[0],
                requestLineParts[1],
                headers,
                new MemoryStream(bodyBytes, writable: false),
                remoteEndPoint);
        }

        public static async UniTask WriteResponseAsync(Stream stream, LoopbackHttpResponse response, byte[] bodyBytes, CancellationToken ct)
        {
            int statusCode = response.StatusCode == 0 ? 500 : response.StatusCode;
            long contentLength = response.ContentLength64 ?? bodyBytes.LongLength;
            StringBuilder builder = new();
            builder.Append("HTTP/1.1 ")
                .Append(statusCode)
                .Append(' ')
                .Append(GetReasonPhrase(statusCode))
                .Append("\r\n");

            if (!string.IsNullOrEmpty(response.ContentType))
            {
                builder.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
            }

            builder.Append("Content-Length: ").Append(contentLength).Append("\r\n");
            builder.Append("Connection: close\r\n");

            foreach (KeyValuePair<string, string> header in response.Headers)
            {
                if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(header.Key, "Connection", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                builder.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
            }

            builder.Append("\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(builder.ToString());
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct);
            if (bodyBytes.Length > 0)
            {
                await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length, ct);
            }

            await stream.FlushAsync(ct);
        }

        private static async UniTask<byte[]> ReadBodyAsync(BufferedByteReader reader, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        {
            if (headers.TryGetValue("Transfer-Encoding", out string transferEncoding) &&
                transferEncoding.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return await ReadChunkedBodyAsync(reader, ct);
            }

            int contentLength = 0;
            if (headers.TryGetValue("Content-Length", out string rawContentLength) &&
                !string.IsNullOrWhiteSpace(rawContentLength) &&
                !int.TryParse(rawContentLength, out contentLength))
            {
                throw new InvalidDataException("Invalid Content-Length header");
            }

            return contentLength > 0
                ? await reader.ReadExactlyAsync(contentLength, ct)
                : Array.Empty<byte>();
        }

        private static async UniTask<byte[]> ReadChunkedBodyAsync(BufferedByteReader reader, CancellationToken ct)
        {
            MemoryStream body = new();

            while (true)
            {
                string sizeLine = await reader.ReadLineAsync(ct);
                if (sizeLine == null)
                {
                    throw new InvalidDataException("Unexpected end of chunked body");
                }

                int extensionIndex = sizeLine.IndexOf(';');
                string rawSize = extensionIndex >= 0 ? sizeLine.Substring(0, extensionIndex) : sizeLine;
                if (!int.TryParse(rawSize.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int chunkSize))
                {
                    throw new InvalidDataException("Invalid chunk size");
                }

                if (chunkSize == 0)
                {
                    while (true)
                    {
                        string trailerLine = await reader.ReadLineAsync(ct);
                        if (string.IsNullOrEmpty(trailerLine))
                        {
                            return body.ToArray();
                        }
                    }
                }

                byte[] chunk = await reader.ReadExactlyAsync(chunkSize, ct);
                await body.WriteAsync(chunk, 0, chunk.Length, ct);

                string lineBreak = await reader.ReadLineAsync(ct);
                if (lineBreak == null)
                {
                    throw new InvalidDataException("Missing chunk terminator");
                }

                if (lineBreak.Length != 0)
                {
                    throw new InvalidDataException("Invalid chunk terminator");
                }
            }
        }

        private static int FindHeaderEnd(byte[] buffer, int length)
        {
            for (int index = 0; index <= length - 4; index++)
            {
                if (buffer[index] == '\r' &&
                    buffer[index + 1] == '\n' &&
                    buffer[index + 2] == '\r' &&
                    buffer[index + 3] == '\n')
                {
                    return index;
                }
            }

            return -1;
        }

        private static string GetReasonPhrase(int statusCode)
        {
            switch (statusCode)
            {
                case 200:
                    return "OK";
                case 202:
                    return "Accepted";
                case 400:
                    return "Bad Request";
                case 403:
                    return "Forbidden";
                case 404:
                    return "Not Found";
                case 405:
                    return "Method Not Allowed";
                case 406:
                    return "Not Acceptable";
                case 500:
                    return "Internal Server Error";
                default:
                    return "HTTP Response";
            }
        }

        private sealed class BufferedByteReader
        {
            private readonly Stream _stream;
            private readonly Queue<byte> _buffer;
            private byte[] _readBuffer;

            public BufferedByteReader(Stream stream, byte[] initialBytes)
            {
                _stream = stream ?? throw new ArgumentNullException(nameof(stream));
                _buffer = initialBytes != null
                    ? new Queue<byte>(initialBytes)
                    : new Queue<byte>();
                _readBuffer = new byte[4096];
            }

            public async UniTask<string> ReadLineAsync(CancellationToken ct)
            {
                MemoryStream lineBytes = new();
                bool sawCarriageReturn = false;

                while (true)
                {
                    int nextByte = await ReadByteAsync(ct);
                    if (nextByte < 0)
                    {
                        if (lineBytes.Length == 0 && !sawCarriageReturn)
                        {
                            return null;
                        }

                        throw new InvalidDataException("Unexpected end of HTTP stream");
                    }

                    if (sawCarriageReturn)
                    {
                        if (nextByte == '\n')
                        {
                            return Encoding.ASCII.GetString(lineBytes.ToArray());
                        }

                        lineBytes.WriteByte((byte)'\r');
                        sawCarriageReturn = false;
                    }

                    if (nextByte == '\r')
                    {
                        sawCarriageReturn = true;
                        continue;
                    }

                    lineBytes.WriteByte((byte)nextByte);
                }
            }

            public async UniTask<byte[]> ReadExactlyAsync(int count, CancellationToken ct)
            {
                byte[] result = new byte[count];
                for (int index = 0; index < count; index++)
                {
                    int nextByte = await ReadByteAsync(ct);
                    if (nextByte < 0)
                    {
                        throw new InvalidDataException("Unexpected end of HTTP stream");
                    }

                    result[index] = (byte)nextByte;
                }

                return result;
            }

            private async UniTask<int> ReadByteAsync(CancellationToken ct)
            {
                if (_buffer.Count == 0)
                {
                    int bytesRead = await _stream.ReadAsync(_readBuffer, 0, _readBuffer.Length, ct);
                    if (bytesRead == 0)
                    {
                        return -1;
                    }

                    for (int index = 0; index < bytesRead; index++)
                    {
                        _buffer.Enqueue(_readBuffer[index]);
                    }
                }

                return _buffer.Dequeue();
            }
        }
    }
}