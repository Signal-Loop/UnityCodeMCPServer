using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Settings;
using UnityEngine;

namespace UnityCodeMcpServer.Servers.StreamableHttp
{
    /// <summary>
    /// Writes Server-Sent Events (SSE) to an HTTP response stream.
    /// Handles proper SSE formatting, keep-alive pings, and graceful disposal.
    /// </summary>
    public sealed class SseStreamWriter : IDisposable
    {
        private readonly Stream _outputStream;
        private readonly HttpListenerResponse _response;
        private readonly object _disposeLock = new object();
        private readonly Encoding _utf8NoBom = new UTF8Encoding(false);
        private long _eventCounter;
        private bool _disposed;
        private bool _headersSent;

        /// <summary>
        /// Whether this writer has been disposed
        /// </summary>
        public bool IsDisposed => _disposed;

        /// <summary>
        /// Creates a new SSE stream writer for the given HTTP response
        /// </summary>
        /// <param name="response">The HTTP response to write to</param>
        public SseStreamWriter(HttpListenerResponse response)
        {
            _response = response ?? throw new ArgumentNullException(nameof(response));
            _outputStream = response.OutputStream;
        }

        /// <summary>
        /// Initialize the SSE stream by setting appropriate headers.
        /// Must be called before writing any events.
        /// </summary>
        public void Initialize()
        {
            if (_headersSent)
                return;

            _response.StatusCode = (int)HttpStatusCode.OK;
            _response.ContentType = McpHttpTransport.ContentTypeSse;
            _response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
            _response.Headers.Add("Connection", "keep-alive");
            _response.Headers.Add("X-Accel-Buffering", "no"); // Disable nginx buffering
            _response.Headers.Add("Access-Control-Allow-Origin", "*");
            _response.Headers.Add("Access-Control-Expose-Headers", McpHttpTransport.SessionIdHeader);
            _response.SendChunked = true;

            _headersSent = true;
        }

        /// <summary>
        /// Write an SSE event with the given data
        /// </summary>
        /// <param name="data">The data payload (will be JSON)</param>
        /// <param name="eventId">Optional event ID for resumability</param>
        /// <param name="eventType">Event type (default: "message")</param>
        /// <param name="ct">Cancellation token</param>
        public async Task WriteEventAsync(string data, string eventId = null, string eventType = null, CancellationToken ct = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SseStreamWriter));

            if (string.IsNullOrEmpty(data))
                throw new ArgumentNullException(nameof(data));

            var sb = new StringBuilder();

            // Event type (optional, defaults to "message" on client side)
            if (!string.IsNullOrEmpty(eventType))
            {
                sb.Append("event: ").Append(eventType).Append('\n');
            }

            // Event ID (optional, for resumability)
            if (!string.IsNullOrEmpty(eventId))
            {
                sb.Append("id: ").Append(eventId).Append('\n');
            }

            // Data - handle multi-line by prefixing each line with "data: "
            foreach (var line in data.Split('\n'))
            {
                sb.Append("data: ").Append(line).Append('\n');
            }

            // End of event (double newline)
            sb.Append('\n');

            await WriteRawAsync(sb.ToString(), ct);
        }

        /// <summary>
        /// Write a JSON-RPC message as an SSE event
        /// </summary>
        /// <param name="jsonRpcMessage">The serialized JSON-RPC message</param>
        /// <param name="generateEventId">Whether to generate an event ID</param>
        /// <param name="ct">Cancellation token</param>
        public async Task WriteMessageAsync(string jsonRpcMessage, bool generateEventId = false, CancellationToken ct = default)
        {
            string eventId = null;
            if (generateEventId)
            {
                eventId = Interlocked.Increment(ref _eventCounter).ToString();
            }

            await WriteEventAsync(jsonRpcMessage, eventId, McpHttpTransport.SseEventMessage, ct);

            if (UnityCodeMcpServerSettings.Instance.VerboseLogging)
            {
                Debug.Log($"{McpProtocol.LogPrefix} [HTTP] SSE sent: {jsonRpcMessage}");
            }
        }

        /// <summary>
        /// Write a keep-alive comment to keep the connection open.
        /// SSE comments start with a colon and are ignored by clients.
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        public async Task WriteKeepAliveAsync(CancellationToken ct = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SseStreamWriter));

            await WriteRawAsync(": keepalive\n\n", ct);

            if (UnityCodeMcpServerSettings.Instance.VerboseLogging)
            {
                Debug.Log($"{McpProtocol.LogPrefix} [HTTP] SSE keepalive sent");
            }
        }

        /// <summary>
        /// Write a retry directive to tell the client how long to wait before reconnecting
        /// </summary>
        /// <param name="milliseconds">Retry interval in milliseconds</param>
        /// <param name="ct">Cancellation token</param>
        public async Task WriteRetryAsync(int milliseconds, CancellationToken ct = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SseStreamWriter));

            await WriteRawAsync($"retry: {milliseconds}\n\n", ct);
        }

        /// <summary>
        /// Flush the stream to ensure data is sent immediately
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        public async Task FlushAsync(CancellationToken ct = default)
        {
            if (_disposed)
                return;

            try
            {
                await _outputStream.FlushAsync(ct);
            }
            catch (ObjectDisposedException)
            {
                // Stream already closed
            }
            catch (IOException)
            {
                // Connection lost
            }
        }

        /// <summary>
        /// Write raw SSE-formatted text to the stream
        /// </summary>
        private async Task WriteRawAsync(string text, CancellationToken ct)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SseStreamWriter));

            var bytes = _utf8NoBom.GetBytes(text);

            try
            {
                await _outputStream.WriteAsync(bytes, 0, bytes.Length, ct);
                await _outputStream.FlushAsync(ct);
            }
            catch (ObjectDisposedException)
            {
                _disposed = true;
                throw;
            }
            catch (IOException ex)
            {
                // Connection lost - mark as disposed
                _disposed = true;
                throw new IOException("SSE stream connection lost", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            lock (_disposeLock)
            {
                if (_disposed)
                    return;
                _disposed = true;
            }

            try
            {
                _outputStream?.Close();
            }
            catch
            {
                // Ignore errors during cleanup
            }

            try
            {
                _response?.Close();
            }
            catch
            {
                // Ignore errors during cleanup
            }
        }
    }
}
