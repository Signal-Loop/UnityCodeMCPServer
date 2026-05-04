using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityCodeMcpServer.Handlers;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Registry;
using UnityCodeMcpServer.Settings;
using UnityEditor;
using UnityEngine;

namespace UnityCodeMcpServer.HttpServer
{
    /// <summary>
    /// Streamable HTTP Server that handles MCP protocol connections per specification 2025-03-26.
    /// Auto-starts with Unity Editor and handles domain reloads gracefully.
    /// </summary>
    [InitializeOnLoad]
    public static class UnityCodeMcpHttpServer
    {
        private const int StartupRetryCount = 3;
        private const int StartupRetryDelayMs = 50;
        private static readonly TimeSpan HeaderReadTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan BodyReadTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan RequestHandlingTimeout = TimeSpan.FromSeconds(120);
        private static readonly TimeSpan ConnectionLifetimeTimeout = TimeSpan.FromSeconds(125);
        private static readonly TimeSpan MainThreadHealthProbeTimeout = TimeSpan.FromMilliseconds(500);
        private static readonly object _stateLock = new();

        private static LoopbackHttpServerTransport _transport;
        private static CancellationTokenSource _serverCts;
        private static McpRegistry _registry;
        private static McpMessageHandler _messageHandler;
        private static HttpRequestHandler _requestHandler;
        private static DateTime? _serverStartedUtc;
        private static DateTime? _lastRequestStartUtc;
        private static DateTime? _lastRequestEndUtc;
        private static DateTime? _lastSuccessfulResponseWriteUtc;
        private static string _lastUnhandledRequestException;
        private static int _pendingRestartAfterAcceptLoopFault;
        private static int _listeningPort;

        static UnityCodeMcpHttpServer()
        {
            // Don't start server in batch mode (AssetImportWorkers, build processes, etc.)
            if (Application.isBatchMode)
            {
                return;
            }

            // Subscribe to editor events
            EditorApplication.quitting += OnEditorQuitting;
            EditorApplication.update += OnEditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        private static void OnEditorUpdate()
        {
            if (Interlocked.Exchange(ref _pendingRestartAfterAcceptLoopFault, 0) == 0)
            {
                return;
            }

            UnityCodeMcpServerLogger.Warn("[UnityCodeMcpHttpServer] Restarting server after accept loop fault");
            RestartServer();
        }

        private static void OnEditorQuitting()
        {
            UnityCodeMcpServerLogger.Debug($"[UnityCodeMcpHttpServer] Editor quitting");
            StopServer("editor-quitting");
        }

        private static void OnBeforeAssemblyReload()
        {
            UnityCodeMcpServerLogger.Debug($"[UnityCodeMcpHttpServer] OnBeforeAssemblyReload event");
            StopServer("assembly-reload");
        }

        private static void OnAfterAssemblyReload()
        {
            UnityCodeMcpServerLogger.Debug($"[UnityCodeMcpHttpServer] Assembly reload completed");
            StartServer("assembly-reload");
        }

        /// <summary>
        /// Start the HTTP server
        /// </summary>
        public static void StartServer()
        {
            StartServer("requested");
        }

        private static void StartServer(string reason)
        {
            UnityCodeMcpServerSettings settings = UnityCodeMcpServerSettings.Instance;

            if (_transport != null)
            {
                UnityCodeMcpServerLogger.Debug($"[UnityCodeMcpHttpServer] Start skipped because transport already exists reason={reason}");
                return;
            }

            string prefix = $"http://127.0.0.1:{settings.HttpPort}/mcp/";

            try
            {
                UnityCodeMcpServerLogger.RefreshSettingsCache();

                // Initialize registry and handlers
                _registry = new McpRegistry();
                _registry.DiscoverAndRegisterAll();
                _messageHandler = new McpMessageHandler(_registry);
                _requestHandler = new HttpRequestHandler(_messageHandler, BuildHealthResponseAsync);

                _serverCts = new CancellationTokenSource();
                _transport = StartTransportWithRetry(settings.HttpPort, settings.Backlog);
                _listeningPort = settings.HttpPort;
                ResetServerState();

                UnityCodeMcpServerLogger.Info($"[UnityCodeMcpHttpServer] Server started on {prefix}\n{BuildRegistrySummary()}");
            }
            catch (SocketException ex) when (IsRetryableBindFailure(ex))
            {
                UnityCodeMcpServerLogger.Error($"[UnityCodeMcpHttpServer] Port {settings.HttpPort} is unavailable ({ex.SocketErrorCode}): {ex.Message}");
                CleanupFailedStart();
            }
            catch (Exception ex)
            {
                UnityCodeMcpServerLogger.Error($"[UnityCodeMcpHttpServer] Failed to start server: {prefix} {ex.Message}");
                CleanupFailedStart();
            }
        }

        /// <summary>
        /// Stop the HTTP server gracefully
        /// </summary>
        public static void StopServer()
        {
            StopServer("requested");
        }

        public static void StopServer(string reason)
        {
            if (_transport == null && _serverCts == null)
            {
                return;
            }

            UnityCodeMcpServerLogger.Debug($"[UnityCodeMcpHttpServer] Stopping server reason={reason}");

            // Cancel all pending operations
            _serverCts?.Cancel();

            try
            {
                _transport?.Stop();
            }
            catch (Exception ex)
            {
                UnityCodeMcpServerLogger.Warn($"[UnityCodeMcpHttpServer] Error during transport cleanup: {ex.Message}");
            }
            finally
            {
                _transport = null;
            }

            try
            {
                _serverCts?.Dispose();
            }
            catch (Exception ex)
            {
                UnityCodeMcpServerLogger.Warn($"[UnityCodeMcpHttpServer] Error disposing CTS: {ex.Message}");
            }
            finally
            {
                _serverCts = null;
            }

            _requestHandler = null;
            _messageHandler = null;
            _registry = null;
            _listeningPort = 0;
            ClearServerState();

            UnityCodeMcpServerLogger.Debug($"[UnityCodeMcpHttpServer] Server stopped reason={reason}");
        }

        #region Menu Items

        /// <summary>
        /// Force refresh the registry
        /// </summary>
        [MenuItem("Tools/UnityCodeMcpServer/HTTP/Refresh Registry")]
        public static void RefreshRegistry()
        {
            _registry?.DiscoverAndRegisterAll();
            UnityCodeMcpServerLogger.Info($"[UnityCodeMcpHttpServer] Registry refreshed");
        }

        /// <summary>
        /// Restart the HTTP server
        /// </summary>
        [MenuItem("Tools/UnityCodeMcpServer/HTTP/Restart Server")]
        public static void RestartServer()
        {
            if (_transport != null)
            {
                StopServer("restart-requested");
            }

            StartServer("restart-requested");
        }

        /// <summary>
        /// Log server status
        /// </summary>
        [MenuItem("Tools/UnityCodeMcpServer/HTTP/Log Server Status")]
        public static void LogServerStatus()
        {
            UnityCodeMcpServerSettings settings = UnityCodeMcpServerSettings.Instance;

            string status = _transport != null && _transport.IsListening ? "Running" : "Stopped";
            bool acceptLoopRunning = _transport != null && _transport.IsAcceptLoopRunning;

            UnityCodeMcpServerLogger.Info($"[UnityCodeMcpHttpServer] Server Status:\n" +
                $"  Status: {status}\n" +
                $"  Port: {settings.HttpPort}\n" +
                $"  ListenerBound: {_transport != null && _transport.IsListening}\n" +
                $"  AcceptLoopRunning: {acceptLoopRunning}\n" +
                $"  ActiveConnections: {_transport?.ActiveConnections ?? 0}\n" +
                $"  StaleConnections: {_transport?.StaleConnections ?? 0}");
        }

        /// <summary>
        /// Log MCP configuration for Streamable HTTP transport
        /// </summary>
        [MenuItem("Tools/UnityCodeMcpServer/HTTP/Print MCP configuration to console")]
        public static void LogMcpConfiguration()
        {
            UnityCodeMcpServerSettings settings = UnityCodeMcpServerSettings.Instance;

            // Configuration for direct HTTP connection (no proxy needed)
            string template = $@"{{
  ""mcpServers"": {{
    ""unity-code-mcp-http"": {{
        ""type"": ""http"",
        ""url"": ""http://127.0.0.1:{settings.HttpPort}{McpHttpTransport.EndpointPath}""
    }}
  }}
}}";

            Debug.Log($"[UnityCodeMcpHttpServer] MCP Configuration (Streamable HTTP):\n{template}");
        }

        #endregion

        #region Public Properties

        private static void CleanupFailedStart()
        {
            try
            {
                _transport?.Dispose();
            }
            catch
            {
                // Ignore cleanup errors for failed starts.
            }
            finally
            {
                _transport = null;
            }

            try
            {
                _serverCts?.Dispose();
            }
            catch
            {
                // Ignore cleanup errors for failed starts.
            }
            finally
            {
                _serverCts = null;
            }

            _requestHandler = null;
            _messageHandler = null;
            _registry = null;
        }

        private static string BuildRegistrySummary()
        {
            if (_registry == null)
            {
                return "Tools: 0\nPrompts: 0\nResources: 0";
            }

            List<string> toolNames = _registry.SyncTools.Keys.Concat(_registry.AsyncTools.Keys).OrderBy(name => name).ToList();
            List<string> promptNames = _registry.Prompts.Keys.OrderBy(name => name).ToList();
            List<string> resourceNames = _registry.Resources.Keys.OrderBy(name => name).ToList();

            return $"Tools: {toolNames.Count} ({string.Join(", ", toolNames)})\n" +
                   $"Prompts: {promptNames.Count} ({string.Join(", ", promptNames)})\n" +
                   $"Resources: {resourceNames.Count} ({string.Join(", ", resourceNames)})";
        }

        private static void ResetServerState()
        {
            lock (_stateLock)
            {
                _serverStartedUtc = DateTime.UtcNow;
                _lastRequestStartUtc = null;
                _lastRequestEndUtc = null;
                _lastSuccessfulResponseWriteUtc = null;
                _lastUnhandledRequestException = null;
            }
        }

        private static void ClearServerState()
        {
            lock (_stateLock)
            {
                _serverStartedUtc = null;
                _lastRequestStartUtc = null;
                _lastRequestEndUtc = null;
                _lastSuccessfulResponseWriteUtc = null;
                _lastUnhandledRequestException = null;
            }
        }

        private static void RecordRequestStart()
        {
            lock (_stateLock)
            {
                _lastRequestStartUtc = DateTime.UtcNow;
            }
        }

        private static void RecordRequestEnd()
        {
            lock (_stateLock)
            {
                _lastRequestEndUtc = DateTime.UtcNow;
            }
        }

        private static void RecordResponseWrite()
        {
            lock (_stateLock)
            {
                _lastSuccessfulResponseWriteUtc = DateTime.UtcNow;
            }
        }

        private static void RecordUnhandledRequestException(Exception ex)
        {
            lock (_stateLock)
            {
                _lastRequestEndUtc = DateTime.UtcNow;
                _lastUnhandledRequestException = ex.ToString();
            }
        }

        private static LoopbackHttpServerTransport StartTransportWithRetry(int port, int backlog)
        {
            Exception lastException = null;

            for (int attempt = 1; attempt <= StartupRetryCount; attempt++)
            {
                LoopbackHttpServerTransport transport = null;

                try
                {
                    transport = new LoopbackHttpServerTransport(
                        IPAddress.Loopback,
                        port,
                        HandleClientAsync,
                        Math.Max(1, backlog),
                        OnAcceptLoopFaulted);
                    transport.Start();
                    return transport;
                }
                catch (SocketException ex) when (IsRetryableBindFailure(ex))
                {
                    lastException = ex;
                    transport?.Dispose();

                    if (attempt < StartupRetryCount)
                    {
                        Thread.Sleep(StartupRetryDelayMs);
                    }
                }
            }

            throw lastException ?? new SocketException((int)SocketError.AddressAlreadyInUse);
        }

        private static bool IsRetryableBindFailure(SocketException ex)
        {
            return ex.SocketErrorCode == SocketError.AddressAlreadyInUse ||
                   ex.SocketErrorCode == SocketError.AccessDenied;
        }

        private static void OnAcceptLoopFaulted(Exception ex)
        {
            UnityCodeMcpServerLogger.Error($"[UnityCodeMcpHttpServer] Accept loop fault detected: {ex}");
            Interlocked.Exchange(ref _pendingRestartAfterAcceptLoopFault, 1);
        }

        private static async UniTask HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            NetworkStream stream = client.GetStream();
            EndPoint remoteEndPoint = client.Client.RemoteEndPoint;
            RecordRequestStart();
            UnityCodeMcpServerLogger.Trace($"[UnityCodeMcpHttpServer] Request parsing start remote={remoteEndPoint}");

            using CancellationTokenSource connectionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectionCts.CancelAfter(ConnectionLifetimeTimeout);

            try
            {
                LoopbackHttpRequest request = await LoopbackHttpProtocol.ReadRequestAsync(
                    stream,
                    remoteEndPoint,
                    connectionCts.Token,
                    HeaderReadTimeout,
                    BodyReadTimeout);
                if (request == null)
                {
                    RecordRequestEnd();
                    return;
                }

                MemoryStream responseBuffer = new();
                LoopbackHttpContext context = new(
                    request,
                    new LoopbackHttpResponse(responseBuffer));

                await RunWithTimeoutAsync(
                    token => _requestHandler.HandleRequestAsync(context, token),
                    RequestHandlingTimeout,
                    "request handling",
                    remoteEndPoint,
                    connectionCts.Token);

                await LoopbackHttpProtocol.WriteResponseAsync(stream, context.Response, responseBuffer.ToArray(), connectionCts.Token);
                RecordResponseWrite();
                RecordRequestEnd();
                UnityCodeMcpServerLogger.Trace($"[UnityCodeMcpHttpServer] Request completed remote={remoteEndPoint} status={context.Response.StatusCode}");
            }
            catch (TimeoutException ex)
            {
                _transport?.RecordStaleConnection();
                RecordUnhandledRequestException(ex);
                UnityCodeMcpServerLogger.Warn($"[UnityCodeMcpHttpServer] Request timeout remote={remoteEndPoint}: {ex.Message}");
                await WritePlainTextResponseAsync(stream, 408, ex.Message, ct);
            }
            catch (OperationCanceledException ex) when (connectionCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                TimeoutException timeoutException = new($"Timed out while servicing request for {remoteEndPoint}", ex);
                _transport?.RecordStaleConnection();
                RecordUnhandledRequestException(timeoutException);
                UnityCodeMcpServerLogger.Warn($"[UnityCodeMcpHttpServer] Connection lifetime timeout remote={remoteEndPoint}: {timeoutException.Message}");
                await WritePlainTextResponseAsync(stream, 408, timeoutException.Message, ct);
            }
            catch (InvalidDataException ex)
            {
                RecordUnhandledRequestException(ex);
                UnityCodeMcpServerLogger.Warn($"[UnityCodeMcpHttpServer] Request parse failure remote={remoteEndPoint}: {ex.Message}");
                await WritePlainTextResponseAsync(stream, 400, ex.Message, ct);
            }
            catch (Exception ex)
            {
                RecordUnhandledRequestException(ex);
                if (!ct.IsCancellationRequested)
                {
                    UnityCodeMcpServerLogger.Error($"[UnityCodeMcpHttpServer] Unhandled request error: {ex}");
                }

                try
                {
                    await WritePlainTextResponseAsync(stream, 500, "Internal Server Error", ct);
                }
                catch
                {
                    // Ignore response write errors after transport failure.
                }
            }
        }

        private static async UniTask RunWithTimeoutAsync(
            Func<CancellationToken, UniTask> action,
            TimeSpan timeout,
            string stage,
            EndPoint remoteEndPoint,
            CancellationToken ct)
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            try
            {
                await action(timeoutCts.Token);
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"Timed out during {stage} for {remoteEndPoint}", ex);
            }
        }

        private static async UniTask<string> BuildHealthResponseAsync(CancellationToken ct)
        {
            LoopbackHttpServerTransport transport = _transport;
            bool mainThreadResponsive = await ProbeMainThreadResponsiveAsync(ct);

            DateTime? serverStartedUtc;
            DateTime? lastRequestStartUtc;
            DateTime? lastRequestEndUtc;
            DateTime? lastSuccessfulResponseWriteUtc;
            string lastUnhandledRequestException;

            lock (_stateLock)
            {
                serverStartedUtc = _serverStartedUtc;
                lastRequestStartUtc = _lastRequestStartUtc;
                lastRequestEndUtc = _lastRequestEndUtc;
                lastSuccessfulResponseWriteUtc = _lastSuccessfulResponseWriteUtc;
                lastUnhandledRequestException = _lastUnhandledRequestException;
            }

            var payload = new
            {
                status = transport != null && transport.IsListening && transport.IsAcceptLoopRunning && mainThreadResponsive ? "ok" : "degraded",
                listenerBound = transport != null && transport.IsListening,
                acceptLoopRunning = transport != null && transport.IsAcceptLoopRunning,
                mainThreadResponsive,
                port = _listeningPort,
                projectPath = GetProjectPath(),
                activeConnections = transport?.ActiveConnections ?? 0,
                staleConnections = transport?.StaleConnections ?? 0,
                lastAcceptUtc = FormatUtc(transport?.LastAcceptUtc),
                lastRequestStartUtc = FormatUtc(lastRequestStartUtc),
                lastRequestEndUtc = FormatUtc(lastRequestEndUtc),
                lastSuccessfulResponseWriteUtc = FormatUtc(lastSuccessfulResponseWriteUtc),
                lastUnhandledAcceptLoopException = transport?.LastUnhandledAcceptLoopException,
                lastUnhandledRequestException,
                serverStartedUtc = FormatUtc(serverStartedUtc),
                registryCounts = new
                {
                    syncTools = _registry?.SyncTools.Count ?? 0,
                    asyncTools = _registry?.AsyncTools.Count ?? 0,
                    prompts = _registry?.Prompts.Count ?? 0,
                    resources = _registry?.Resources.Count ?? 0
                }
            };

            return JsonSerializer.Serialize(payload);
        }

        private static async UniTask<bool> ProbeMainThreadResponsiveAsync(CancellationToken ct)
        {
            using CancellationTokenSource probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCts.CancelAfter(MainThreadHealthProbeTimeout);

            try
            {
                await UniTask.SwitchToMainThread(probeCts.Token);
                await UniTask.SwitchToThreadPool();
                return true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return false;
            }
        }

        private static string FormatUtc(DateTime? value)
        {
            return value?.ToString("O");
        }

        private static string GetProjectPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static async UniTask WritePlainTextResponseAsync(NetworkStream stream, int statusCode, string message, CancellationToken ct)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(message ?? string.Empty);
            LoopbackHttpResponse response = new(new MemoryStream())
            {
                StatusCode = statusCode,
                ContentType = "text/plain"
            };

            await LoopbackHttpProtocol.WriteResponseAsync(stream, response, bodyBytes, ct);
        }

        #endregion
    }
}
