using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityCodeMcpServer.Handlers;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Settings;
using UnityEngine;

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
    /// Implements routing, session validation, and request processing.
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
                if (UnityCodeMcpServerSettings.Instance.VerboseLogging)
                {
                    Debug.Log($"{McpProtocol.LogPrefix} [HTTP] {request.HttpMethod} {request.Url.PathAndQuery} from {request.RemoteEndPoint}");
                }

                // Validate Origin header for security
                var originValidation = ValidateOrigin(request);
                if (!originValidation.IsValid)
                {
                    await SendErrorResponseAsync(response, originValidation.StatusCode, originValidation.ErrorMessage, ct);
                    return;
                }

                // Route by HTTP method
                switch (request.HttpMethod.ToUpperInvariant())
                {
                    case "POST":
                        await HandlePostAsync(context, ct);
                        break;

                    case "GET":
                        await HandleGetAsync(context, ct);
                        break;

                    case "DELETE":
                        await HandleDeleteAsync(context, ct);
                        break;

                    case "OPTIONS":
                        // CORS preflight
                        await HandleOptionsAsync(context, ct);
                        break;

                    default:
                        response.Headers.Add("Allow", "GET, POST, DELETE, OPTIONS");
                        await SendErrorResponseAsync(response, 405, "Method Not Allowed", ct);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // Server shutting down
            }
            catch (Exception ex)
            {
                Debug.LogError($"{McpProtocol.LogPrefix} [HTTP] Request handler error: {ex}");
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
                Debug.LogWarning($"{McpProtocol.LogPrefix} [HTTP] Failed to read request body: {ex.Message}");
                await SendErrorResponseAsync(response, 400, "Failed to read request body", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(requestBody))
            {
                await SendErrorResponseAsync(response, 400, "Empty request body", ct);
                return;
            }

            if (UnityCodeMcpServerSettings.Instance.VerboseLogging)
            {
                Debug.Log($"{McpProtocol.LogPrefix} [HTTP] Received: {requestBody}");
            }

            // Parse to determine if this is an initialize request
            bool isInitializeRequest = false;
            bool isNotification = false;
            try
            {
                using (var doc = JsonDocument.Parse(requestBody))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("method", out var methodProp))
                    {
                        var method = methodProp.GetString();
                        isInitializeRequest = method == McpMethods.Initialize;

                        // Check if notification (no id field)
                        isNotification = !root.TryGetProperty("id", out _);
                    }
                }
            }
            catch (JsonException)
            {
                // Will be handled by message processor
            }

            // Session validation
            var sessionId = request.Headers[McpHttpTransport.SessionIdHeader];

            if (isInitializeRequest)
            {
                // Create new session for initialize requests
                sessionId = _sessionManager.CreateSession();
            }
            else
            {
                // Validate existing session for non-initialize requests
                if (string.IsNullOrEmpty(sessionId))
                {
                    await SendJsonRpcErrorAsync(response, null, JsonRpcErrorCodes.InvalidRequest,
                        "Missing session ID header", 400, ct);
                    return;
                }

                if (!_sessionManager.ValidateSession(sessionId))
                {
                    await SendJsonRpcErrorAsync(response, null, JsonRpcErrorCodes.InvalidRequest,
                        "Invalid or expired session", 404, ct);
                    return;
                }

                _sessionManager.TouchSession(sessionId);
            }

            // Mark session as initialized if this is an initialize request
            if (isInitializeRequest)
            {
                var session = _sessionManager.GetSession(sessionId);
                if (session != null)
                {
                    session.IsInitialized = true;
                }
            }

            // Process message on main thread for Unity API access
            await UniTask.SwitchToMainThread();
            var responseJson = await _messageHandler.ProcessMessageAsync(requestBody);

            // Handle notifications - return 202 Accepted with no body
            if (isNotification || responseJson == null)
            {
                response.StatusCode = 202;
                response.Headers.Add(McpHttpTransport.SessionIdHeader, sessionId);
                response.Close();
                return;
            }

            // Return JSON response (could also use SSE if client prefers and server wants)
            // For simplicity, we return JSON for single request-response
            await SendJsonResponseAsync(response, responseJson, sessionId, ct);
        }

        /// <summary>
        /// Handle GET requests - open SSE stream for server-initiated messages
        /// </summary>
        private async UniTask HandleGetAsync(HttpListenerContext context, CancellationToken ct)
        {
            var request = context.Request;
            var response = context.Response;

            // Check Accept header
            var acceptHeader = request.Headers["Accept"] ?? "";
            if (!acceptHeader.Contains(McpHttpTransport.ContentTypeSse))
            {
                await SendErrorResponseAsync(response, 406,
                    $"Accept header must include {McpHttpTransport.ContentTypeSse}", ct);
                return;
            }

            // Session validation
            var sessionId = request.Headers[McpHttpTransport.SessionIdHeader];
            SessionState session = null;

            if (!string.IsNullOrEmpty(sessionId))
            {
                session = _sessionManager.GetSession(sessionId);
                if (session == null)
                {
                    await SendErrorResponseAsync(response, 404, "Session not found or expired", ct);
                    return;
                }

                if (!session.IsInitialized)
                {
                    await SendErrorResponseAsync(response, 400, "Session not initialized", ct);
                    return;
                }

                _sessionManager.TouchSession(sessionId);
            }
            else
            {
                await SendErrorResponseAsync(response, 400, "Missing session ID header", ct);
                return;
            }

            // Check Last-Event-ID for resumability (optional feature)
            var lastEventId = request.Headers["Last-Event-ID"];
            if (!string.IsNullOrEmpty(lastEventId) && UnityCodeMcpServerSettings.Instance.VerboseLogging)
            {
                Debug.Log($"{McpProtocol.LogPrefix} [HTTP] Client attempting to resume from event: {lastEventId}");
            }

            // Create SSE stream
            var sseWriter = new SseStreamWriter(response);

            try
            {
                sseWriter.Initialize();
                response.Headers.Add(McpHttpTransport.SessionIdHeader, sessionId);

                // Close any existing SSE stream before setting new one
                session.CloseSseStream();
                session.SetSseStream(sseWriter);

                if (UnityCodeMcpServerSettings.Instance.VerboseLogging)
                {
                    Debug.Log($"{McpProtocol.LogPrefix} [HTTP] SSE stream opened for session: {sessionId}");
                }

                // Keep connection alive with periodic pings
                var keepAliveInterval = Math.Max(5, UnityCodeMcpServerSettings.Instance.SseKeepAliveIntervalSeconds);
                try
                {
                    using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, session.CancellationTokenSource.Token))
                    {
                        // Send initial keepalive immediately to confirm connection
                        await sseWriter.WriteKeepAliveAsync(linkedCts.Token);

                        while (!linkedCts.Token.IsCancellationRequested && !sseWriter.IsDisposed)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(keepAliveInterval), linkedCts.Token);

                            // Touch session on each keepalive to prevent timeout
                            _sessionManager.TouchSession(sessionId);

                            if (!sseWriter.IsDisposed)
                            {
                                await sseWriter.WriteKeepAliveAsync(linkedCts.Token);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when session ends or server stops
                }
                catch (IOException)
                {
                    // Client disconnected - this is normal
                    if (UnityCodeMcpServerSettings.Instance.VerboseLogging)
                    {
                        Debug.Log($"{McpProtocol.LogPrefix} [HTTP] SSE client disconnected for session: {sessionId}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{McpProtocol.LogPrefix} [HTTP] SSE stream error for session {sessionId}: {ex.Message}");
                }
                finally
                {
                    session.CloseSseStream();
                    if (UnityCodeMcpServerSettings.Instance.VerboseLogging)
                    {
                        Debug.Log($"{McpProtocol.LogPrefix} [HTTP] SSE stream closed for session: {sessionId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{McpProtocol.LogPrefix} [HTTP] Failed to setup SSE stream: {ex.Message}");
                sseWriter?.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Handle DELETE requests - terminate session
        /// </summary>
        private async UniTask HandleDeleteAsync(HttpListenerContext context, CancellationToken ct)
        {
            var request = context.Request;
            var response = context.Response;

            var sessionId = request.Headers[McpHttpTransport.SessionIdHeader];
            if (string.IsNullOrEmpty(sessionId))
            {
                await SendErrorResponseAsync(response, 400, "Missing session ID header", ct);
                return;
            }

            var terminated = _sessionManager.TerminateSession(sessionId);
            if (terminated)
            {
                response.StatusCode = 200;
                response.Close();
            }
            else
            {
                await SendErrorResponseAsync(response, 404, "Session not found", ct);
            }
        }

        /// <summary>
        /// Handle OPTIONS requests for CORS preflight
        /// </summary>
        private UniTask HandleOptionsAsync(HttpListenerContext context, CancellationToken ct)
        {
            var request = context.Request;
            var response = context.Response;

            response.StatusCode = 204;

            // Allow the requesting origin if it's localhost
            var origin = request.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin))
            {
                response.Headers.Add("Access-Control-Allow-Origin", origin);
            }
            else
            {
                response.Headers.Add("Access-Control-Allow-Origin", "*");
            }

            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, DELETE, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers",
                $"Content-Type, Accept, {McpHttpTransport.SessionIdHeader}, Last-Event-ID, Cache-Control");
            response.Headers.Add("Access-Control-Expose-Headers", McpHttpTransport.SessionIdHeader);
            response.Headers.Add("Access-Control-Max-Age", "86400");
            response.Close();

            return UniTask.CompletedTask;
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
            Debug.LogWarning($"{McpProtocol.LogPrefix} [HTTP] Blocked request from origin: {origin}");
            return ValidationResult.Failure(403, "Origin not allowed");
        }

        /// <summary>
        /// Send a JSON response
        /// </summary>
        private async Task SendJsonResponseAsync(HttpListenerResponse response, string json, string sessionId, CancellationToken ct)
        {
            response.StatusCode = 200;
            response.ContentType = McpHttpTransport.ContentTypeJson;
            response.Headers.Add(McpHttpTransport.SessionIdHeader, sessionId);
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Expose-Headers", McpHttpTransport.SessionIdHeader);

            var bytes = _utf8NoBom.GetBytes(json);
            response.ContentLength64 = bytes.Length;

            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length, ct);
            response.Close();

            if (UnityCodeMcpServerSettings.Instance.VerboseLogging)
            {
                Debug.Log($"{McpProtocol.LogPrefix} [HTTP] Sent: {json}");
            }
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
