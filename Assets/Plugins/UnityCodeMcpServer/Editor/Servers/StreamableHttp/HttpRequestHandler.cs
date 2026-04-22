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

        private ValidationResult(bool isValid, int statusCode, string errorMessage)
        {
            IsValid = isValid;
            StatusCode = statusCode;
            ErrorMessage = errorMessage;
        }

        public static ValidationResult Success() =>
            new(true, 200, null);

        public static ValidationResult Failure(int statusCode, string errorMessage) =>
            new(false, statusCode, errorMessage);
    }

    /// <summary>
    /// Handles HTTP requests for the MCP Streamable HTTP transport.
    /// This bridge-focused implementation accepts POST request/response traffic only.
    /// </summary>
    public sealed class HttpRequestHandler
    {
        private readonly McpMessageHandler _messageHandler;
        private readonly Encoding _utf8NoBom = new UTF8Encoding(false);

        public HttpRequestHandler(McpMessageHandler messageHandler)
        {
            _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
        }

        /// <summary>
        /// Handle an incoming HTTP request
        /// </summary>
        /// <param name="context">The HTTP listener context</param>
        /// <param name="ct">Cancellation token</param>
        public async UniTask HandleRequestAsync(LoopbackHttpContext context, CancellationToken ct)
        {
            LoopbackHttpRequest request = context.Request;
            LoopbackHttpResponse response = context.Response;

            try
            {
                UnityCodeMcpServerLogger.Trace($"[HTTP] {request.HttpMethod} {request.PathAndQuery} from {request.RemoteEndPoint}");

                if (!IsSupportedPath(request.PathAndQuery))
                {
                    await SendErrorResponseAsync(response, 404, "Not Found", ct);
                    return;
                }

                ValidationResult originValidation = ValidateOrigin(request.GetHeader("Origin"));
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

                response.Headers["Allow"] = "POST";
                await SendErrorResponseAsync(response, 405, "Method Not Allowed", ct);
            }
            catch (OperationCanceledException)
            {
                // Server shutting down
            }
            catch (Exception ex)
            {
                UnityCodeMcpServerLogger.Error($"[HTTP] Request handler error: {ex}");
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

        public async UniTask HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            try
            {
                UnityCodeMcpServerLogger.Trace($"[HTTP] {request.HttpMethod} {request.Url.PathAndQuery} from {request.RemoteEndPoint}");

                // Validate Origin header for security
                ValidationResult originValidation = ValidateOrigin(request);
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
                UnityCodeMcpServerLogger.Error($"[HTTP] Request handler error: {ex}");
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
        private async UniTask HandlePostAsync(LoopbackHttpContext context, CancellationToken ct)
        {
            LoopbackHttpRequest request = context.Request;
            LoopbackHttpResponse response = context.Response;

            string acceptHeader = request.GetHeader("Accept") ?? "";
            bool acceptsJson = acceptHeader.Contains(McpHttpTransport.ContentTypeJson) || acceptHeader.Contains("*/*");
            bool acceptsSse = acceptHeader.Contains(McpHttpTransport.ContentTypeSse);

            if (!acceptsJson && !acceptsSse)
            {
                await SendErrorResponseAsync(response, 406,
                    $"Accept header must include {McpHttpTransport.ContentTypeJson} and/or {McpHttpTransport.ContentTypeSse}", ct);
                return;
            }

            string requestBody;
            try
            {
                using (StreamReader reader = new(request.InputStream, Encoding.UTF8, true, 1024, leaveOpen: true))
                {
                    requestBody = await reader.ReadToEndAsync();
                }
            }
            catch (Exception ex)
            {
                UnityCodeMcpServerLogger.Warn($"[HTTP] Failed to read request body: {ex.Message}");
                await SendErrorResponseAsync(response, 400, "Failed to read request body", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(requestBody))
            {
                await SendErrorResponseAsync(response, 400, "Empty request body", ct);
                return;
            }

            UnityCodeMcpServerLogger.Trace($"[HTTP] Received: {requestBody}");

            bool isNotification = false;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(requestBody))
                {
                    JsonElement root = doc.RootElement;
                    isNotification = !root.TryGetProperty("id", out _);
                }
            }
            catch (JsonException)
            {
                // Will be handled by message processor
            }

            await UniTask.SwitchToMainThread();
            string responseJson = await _messageHandler.ProcessMessageAsync(requestBody);

            if (isNotification || responseJson == null)
            {
                response.StatusCode = 202;
                response.Close();
                return;
            }

            await SendJsonResponseAsync(response, responseJson, ct);
        }

        private async UniTask HandlePostAsync(HttpListenerContext context, CancellationToken ct)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            // Check Accept header
            string acceptHeader = request.Headers["Accept"] ?? "";
            bool acceptsJson = acceptHeader.Contains(McpHttpTransport.ContentTypeJson) || acceptHeader.Contains("*/*");
            bool acceptsSse = acceptHeader.Contains(McpHttpTransport.ContentTypeSse);

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
                using (StreamReader reader = new(request.InputStream, Encoding.UTF8))
                {
                    requestBody = await reader.ReadToEndAsync();
                }
            }
            catch (Exception ex)
            {
                UnityCodeMcpServerLogger.Warn($"[HTTP] Failed to read request body: {ex.Message}");
                await SendErrorResponseAsync(response, 400, "Failed to read request body", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(requestBody))
            {
                await SendErrorResponseAsync(response, 400, "Empty request body", ct);
                return;
            }

            UnityCodeMcpServerLogger.Trace($"[HTTP] Received: {requestBody}");

            bool isNotification = false;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(requestBody))
                {
                    JsonElement root = doc.RootElement;
                    isNotification = !root.TryGetProperty("id", out _);
                }
            }
            catch (JsonException)
            {
                // Will be handled by message processor
            }

            // Process message on main thread for Unity API access
            await UniTask.SwitchToMainThread();
            string responseJson = await _messageHandler.ProcessMessageAsync(requestBody);

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
        private ValidationResult ValidateOrigin(string origin)
        {
            if (string.IsNullOrEmpty(origin))
                return ValidationResult.Success();

            if (origin.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) ||
                origin.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                origin.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase) ||
                origin.StartsWith("https://127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Success();
            }

            UnityCodeMcpServerLogger.Warn($"[HTTP] Blocked request from origin: {origin}");
            return ValidationResult.Failure(403, "Origin not allowed");
        }

        private ValidationResult ValidateOrigin(HttpListenerRequest request)
        {
            return ValidateOrigin(request.Headers["Origin"]);
        }

        private static bool IsSupportedPath(string pathAndQuery)
        {
            if (string.IsNullOrEmpty(pathAndQuery))
            {
                return false;
            }

            int queryIndex = pathAndQuery.IndexOfAny(new[] { '?', '#' });
            string path = queryIndex >= 0 ? pathAndQuery.Substring(0, queryIndex) : pathAndQuery;
            return string.Equals(path, "/mcp", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(path, McpHttpTransport.EndpointPath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Send a JSON response
        /// </summary>
        private async Task SendJsonResponseAsync(LoopbackHttpResponse response, string json, CancellationToken ct)
        {
            response.StatusCode = 200;
            response.ContentType = McpHttpTransport.ContentTypeJson;
            response.Headers["Access-Control-Allow-Origin"] = "*";
            byte[] bytes = _utf8NoBom.GetBytes(json);
            response.ContentLength64 = bytes.Length;

            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length, ct);
            response.Close();

            UnityCodeMcpServerLogger.Trace($"[HTTP] Sent: {json}");
        }

        /// <summary>
        /// Send a JSON response
        /// </summary>
        private async Task SendJsonResponseAsync(HttpListenerResponse response, string json, CancellationToken ct)
        {
            response.StatusCode = 200;
            response.ContentType = McpHttpTransport.ContentTypeJson;
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            byte[] bytes = _utf8NoBom.GetBytes(json);
            response.ContentLength64 = bytes.Length;

            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length, ct);
            response.Close();

            UnityCodeMcpServerLogger.Trace($"[HTTP] Sent: {json}");
        }

        /// <summary>
        /// Send a plain text error response
        /// </summary>
        private async Task SendErrorResponseAsync(LoopbackHttpResponse response, int statusCode, string message, CancellationToken ct)
        {
            response.StatusCode = statusCode;
            response.ContentType = "text/plain";

            byte[] bytes = _utf8NoBom.GetBytes(message ?? string.Empty);
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
                response.Close();
            }
        }

        private async Task SendErrorResponseAsync(HttpListenerResponse response, int statusCode, string message, CancellationToken ct)
        {
            response.StatusCode = statusCode;
            response.ContentType = "text/plain";

            byte[] bytes = _utf8NoBom.GetBytes(message);
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
