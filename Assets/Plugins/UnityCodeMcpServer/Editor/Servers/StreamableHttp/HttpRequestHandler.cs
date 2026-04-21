using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityCodeMcpServer.Handlers;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Settings;

namespace UnityCodeMcpServer.Servers.StreamableHttp
{
    /// <summary>
    /// Result of request validation
    /// </summary>
    public readonly struct ValidationResult
    {
        public bool IsValid { get; }
        public int StatusCode { get; }
        public string ErrorMessage { get; }
        public string SessionId { get; }

        private ValidationResult(bool isValid, int statusCode, string errorMessage, string sessionId)
        {
            IsValid = isValid;
            StatusCode = statusCode;
            ErrorMessage = errorMessage;
            SessionId = sessionId;
        }

        public static ValidationResult Success(string sessionId = null) =>
            new ValidationResult(true, 200, null, sessionId);

        public static ValidationResult Failure(int statusCode, string errorMessage) =>
            new ValidationResult(false, statusCode, errorMessage, null);
    }

    /// <summary>
    /// Handles HTTP requests for the MCP Streamable HTTP transport.
    /// This bridge-focused implementation accepts POST request/response traffic only.
    /// </summary>
    public sealed class HttpRequestHandler
    {
        private readonly McpMessageHandler _messageHandler;
        private readonly SessionManager _sessionManager;
        private readonly Encoding _utf8NoBom = new UTF8Encoding(false);

        public HttpRequestHandler(McpMessageHandler messageHandler, SessionManager sessionManager)
        {
            _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        }

        /// <summary>
        /// Handle an incoming HTTP request
        /// </summary>
        /// <param name="context">The HTTP listener context</param>
        /// <param name="ct">Cancellation token</param>
        public async UniTask HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                LoopLogger.Trace($"{McpProtocol.LogPrefix} [HTTP] {request.HttpMethod} {request.Url.PathAndQuery} from {request.RemoteEndPoint}");

                // Validate Origin header for security
                var originValidation = ValidateOrigin(request);
                if (!originValidation.IsValid)
                {
                    await SendErrorResponseAsync(response, originValidation.StatusCode, originValidation.ErrorMessage, ct);
                    return;
                }

                if (string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandlePostAsync(context, ct);
                    return;
                }

                response.Headers.Add("Allow", "POST");
                await SendErrorResponseAsync(response, 405, "Method Not Allowed", ct);
            }
            catch (OperationCanceledException)
            {
                // Server shutting down
            }
            catch (Exception ex)
            {
                LoopLogger.Error($"{McpProtocol.LogPrefix} [HTTP] Request handler error: {ex}");
                try
                {
                    await SendErrorResponseAsync(response, 500, "Internal Server Error", ct);
                }
                catch
                {
                    // Ignore errors when sending error response
                }
            }
        }

        /// <summary>
        /// Handle POST requests - primary method for client-to-server messages
        /// </summary>
        private async UniTask HandlePostAsync(HttpListenerContext context, CancellationToken ct)
        {
            var request = context.Request;
            var response = context.Response;

            // Check Accept header
            var acceptHeader = request.Headers["Accept"] ?? "";
            var acceptsJson = acceptHeader.Contains(McpHttpTransport.ContentTypeJson) || acceptHeader.Contains("*/*");
            var acceptsSse = acceptHeader.Contains(McpHttpTransport.ContentTypeSse);

            if (!acceptsJson && !acceptsSse)
            {
                await SendErrorResponseAsync(response, 406,
                    $"Accept header must include {McpHttpTransport.ContentTypeJson} and/or {McpHttpTransport.ContentTypeSse}", ct);
                return;
            }

            // Read request body
            string requestBody;
            try
            {
                using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                {
                    requestBody = await reader.ReadToEndAsync();
                }
            }
            catch (Exception ex)
            {
                LoopLogger.Warn($"{McpProtocol.LogPrefix} [HTTP] Failed to read request body: {ex.Message}");
                await SendErrorResponseAsync(response, 400, "Failed to read request body", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(requestBody))
            {
                await SendErrorResponseAsync(response, 400, "Empty request body", ct);
                return;
            }

            LoopLogger.Trace($"{McpProtocol.LogPrefix} [HTTP] Received: {requestBody}");

            bool isNotification = false;
            try
            {
                using (var doc = JsonDocument.Parse(requestBody))
                {
                    var root = doc.RootElement;
                    isNotification = !root.TryGetProperty("id", out _);
                }
            }
            catch (JsonException)
            {
                // Will be handled by message processor
            }

            // Process message on main thread for Unity API access
            await UniTask.SwitchToMainThread();
            var responseJson = await _messageHandler.ProcessMessageAsync(requestBody);

            // Handle notifications - return 202 Accepted with no body
            if (isNotification || responseJson == null)
            {
                response.StatusCode = 202;
                response.Close();
                return;
            }

            // Return JSON response (could also use SSE if client prefers and server wants)
            // For simplicity, we return JSON for single request-response
            await SendJsonResponseAsync(response, responseJson, ct);
        }

        /// <summary>
        /// Validate Origin header for security (prevent DNS rebinding attacks)
        /// </summary>
        private ValidationResult ValidateOrigin(HttpListenerRequest request)
        {
            var origin = request.Headers["Origin"];

            // If no Origin header, allow (same-origin requests)
            if (string.IsNullOrEmpty(origin))
                return ValidationResult.Success();

            // Allow localhost origins
            if (origin.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) ||
                origin.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                origin.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase) ||
                origin.StartsWith("https://127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Success();
            }

            // Block other origins
            LoopLogger.Warn($"{McpProtocol.LogPrefix} [HTTP] Blocked request from origin: {origin}");
            return ValidationResult.Failure(403, "Origin not allowed");
        }

        /// <summary>
        /// Send a JSON response
        /// </summary>
        private async Task SendJsonResponseAsync(HttpListenerResponse response, string json, CancellationToken ct)
        {
            response.StatusCode = 200;
            response.ContentType = McpHttpTransport.ContentTypeJson;
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Expose-Headers", McpHttpTransport.SessionIdHeader);

            var bytes = _utf8NoBom.GetBytes(json);
            response.ContentLength64 = bytes.Length;

            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length, ct);
            response.Close();

            LoopLogger.Trace($"{McpProtocol.LogPrefix} [HTTP] Sent: {json}");
        }

        /// <summary>
        /// Send a JSON-RPC error response
        /// </summary>
        private async Task SendJsonRpcErrorAsync(HttpListenerResponse response, object id, int code, string message, int httpStatus, CancellationToken ct)
        {
            var error = JsonRpcResponse.Failure(id, code, message);
            var json = JsonHelper.Serialize(error);

            response.StatusCode = httpStatus;
            response.ContentType = McpHttpTransport.ContentTypeJson;

            var bytes = _utf8NoBom.GetBytes(json);
            response.ContentLength64 = bytes.Length;

            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length, ct);
            response.Close();
        }

        /// <summary>
        /// Send a plain text error response
        /// </summary>
        private async Task SendErrorResponseAsync(HttpListenerResponse response, int statusCode, string message, CancellationToken ct)
        {
            response.StatusCode = statusCode;
            response.ContentType = "text/plain";

            var bytes = _utf8NoBom.GetBytes(message);
            response.ContentLength64 = bytes.Length;

            try
            {
                await response.OutputStream.WriteAsync(bytes, 0, bytes.Length, ct);
            }
            catch
            {
                // Ignore write errors
            }
            finally
            {
                try
                {
                    response.Close();
                }
                catch
                {
                    // Ignore close errors
                }
            }
        }
    }
}
