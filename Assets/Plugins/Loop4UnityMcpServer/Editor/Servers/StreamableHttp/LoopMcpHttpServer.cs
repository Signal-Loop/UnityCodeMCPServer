using System;
using System.Linq;
using System.Net;
using System.Threading;
using Cysharp.Threading.Tasks;
using LoopMcpServer.Handlers;
using LoopMcpServer.Protocol;
using LoopMcpServer.Registry;
using LoopMcpServer.Settings;
using UnityEditor;
using UnityEngine;

namespace LoopMcpServer.Servers.StreamableHttp
{
    /// <summary>
    /// Streamable HTTP Server that handles MCP protocol connections per specification 2025-03-26.
    /// Auto-starts with Unity Editor and handles domain reloads gracefully.
    /// Runs alongside the TCP server on a separate port.
    /// </summary>
    [InitializeOnLoad]
    public static class LoopMcpHttpServer
    {
        private static HttpListener _listener;
        private static CancellationTokenSource _serverCts;
        private static McpRegistry _registry;
        private static McpMessageHandler _messageHandler;
        private static SessionManager _sessionManager;
        private static HttpRequestHandler _requestHandler;
        private static bool _isRunning;

        static LoopMcpHttpServer()
        {
            // Subscribe to editor events
            EditorApplication.quitting += OnEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

            // Defer server startup to after domain load completes
            // This prevents blocking during "Completing Domain Reload"
            EditorApplication.delayCall += OnDelayedStart;
        }

        private static void OnDelayedStart()
        {
            // Unsubscribe to prevent multiple calls
            EditorApplication.delayCall -= OnDelayedStart;

            // Start the server if enabled
            StartServer();
        }

        /// <summary>
        /// Start the HTTP server
        /// </summary>
        public static void StartServer()
        {
            if (_isRunning)
            {
                Debug.LogWarning($"{McpProtocol.LogPrefix} [HTTP] Server already running");
                return;
            }

            var settings = LoopMcpServerSettings.Instance;

            if (settings.StartupServer != LoopMcpServerSettings.ServerStartupMode.Http)
            {
                if (settings.VerboseLogging)
                {
                    Debug.Log($"{McpProtocol.LogPrefix} [HTTP] Startup skipped because server selection is {settings.StartupServer}");
                }
                return;
            }

            try
            {
                // Initialize registry and handlers
                _registry = new McpRegistry();
                _registry.DiscoverAndRegisterAll(settings.VerboseLogging);
                _messageHandler = new McpMessageHandler(_registry);
                _sessionManager = new SessionManager(
                    settings.SessionTimeoutSeconds,
                    cleanupIntervalSeconds: 60
                );
                _sessionManager.StartCleanupLoop();
                _requestHandler = new HttpRequestHandler(_messageHandler, _sessionManager);

                // Configure and start HTTP listener
                _serverCts = new CancellationTokenSource();
                _listener = new HttpListener();

                // Bind to localhost only (127.0.0.1) for security
                // HttpListener with trailing slash handles both /mcp and /mcp/
                var prefix = $"http://127.0.0.1:{settings.HttpPort}/mcp/";
                _listener.Prefixes.Add(prefix);

                _listener.Start();
                _isRunning = true;

                Debug.Log($"{McpProtocol.LogPrefix} [HTTP] Server started on {prefix}\n{BuildRegistrySummary()}");

                // Start accepting requests
                AcceptRequestsAsync(_serverCts.Token).Forget();
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 5)
            {
                // Access denied - need to run: netsh http add urlacl url=http://127.0.0.1:3001/mcp/ user=Everyone
                Debug.LogError($"{McpProtocol.LogPrefix} [HTTP] Access denied. Run as admin or add URL reservation:\n" +
                    $"netsh http add urlacl url=http://127.0.0.1:{settings.HttpPort}{McpHttpTransport.EndpointPath} user=Everyone");
                _isRunning = false;
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 183)
            {
                // Port already in use
                Debug.LogError($"{McpProtocol.LogPrefix} [HTTP] Port {settings.HttpPort} is already in use");
                _isRunning = false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{McpProtocol.LogPrefix} [HTTP] Failed to start server: {ex.Message}");
                _isRunning = false;
            }
        }

        /// <summary>
        /// Stop the HTTP server gracefully
        /// </summary>
        public static void StopServer()
        {
            if (!_isRunning)
                return;

            if (LoopMcpServerSettings.Instance.VerboseLogging)
            {
                Debug.Log($"{McpProtocol.LogPrefix} [HTTP] Stopping server...");
            }

            // Cancel all pending operations
            _serverCts?.Cancel();

            // Terminate all sessions (closes SSE streams)
            try
            {
                _sessionManager?.TerminateAllSessions();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{McpProtocol.LogPrefix} [HTTP] Error terminating sessions: {ex.Message}");
            }

            // Dispose session manager
            try
            {
                _sessionManager?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{McpProtocol.LogPrefix} [HTTP] Error disposing session manager: {ex.Message}");
            }

            // Stop and close the listener
            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{McpProtocol.LogPrefix} [HTTP] Error during listener cleanup: {ex.Message}");
            }
            finally
            {
                _listener = null;
            }

            // Dispose cancellation token source
            try
            {
                _serverCts?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{McpProtocol.LogPrefix} [HTTP] Error disposing CTS: {ex.Message}");
            }
            finally
            {
                _serverCts = null;
            }

            _sessionManager = null;
            _requestHandler = null;
            _messageHandler = null;
            _registry = null;
            _isRunning = false;

            if (LoopMcpServerSettings.Instance.VerboseLogging)
            {
                Debug.Log($"{McpProtocol.LogPrefix} [HTTP] Server stopped");
            }
        }

        private static void OnEditorQuitting()
        {
            StopServer();
        }

        private static void OnBeforeAssemblyReload()
        {
            StopServer();
        }

        /// <summary>
        /// Main request accept loop
        /// </summary>
        private static async UniTaskVoid AcceptRequestsAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();

                    // Handle request in background (don't await)
                    HandleRequestAsync(context, ct).Forget();
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 995)
                {
                    // The I/O operation was aborted - listener was stopped
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // Listener was disposed
                    break;
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        Debug.LogError($"{McpProtocol.LogPrefix} [HTTP] Error accepting request: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Handle a single HTTP request
        /// </summary>
        private static async UniTaskVoid HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
        {
            try
            {
                await _requestHandler.HandleRequestAsync(context, ct);
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    Debug.LogError($"{McpProtocol.LogPrefix} [HTTP] Unhandled request error: {ex}");
                }
            }
        }

        #region Menu Items

        /// <summary>
        /// Force refresh the registry
        /// </summary>
        [MenuItem("Tools/LoopMcpServer/HTTP/Refresh Registry")]
        public static void RefreshRegistry()
        {
            _registry?.DiscoverAndRegisterAll(LoopMcpServerSettings.Instance.VerboseLogging);
            Debug.Log($"{McpProtocol.LogPrefix} [HTTP] Registry refreshed");
        }

        /// <summary>
        /// Restart the HTTP server
        /// </summary>
        [MenuItem("Tools/LoopMcpServer/HTTP/Restart Server")]
        public static void RestartServer()
        {
            RestartServerAsync().Forget();
        }

        private static async UniTaskVoid RestartServerAsync()
        {
            StopServer();
            // Wait for resources to be fully released
            await UniTask.Delay(200);
            StartServer();
        }

        /// <summary>
        /// Log server status
        /// </summary>
        [MenuItem("Tools/LoopMcpServer/HTTP/Log Server Status")]
        public static void LogServerStatus()
        {
            var settings = LoopMcpServerSettings.Instance;

            var status = _isRunning ? "Running" : "Stopped";
            var sessionCount = _sessionManager?.ActiveSessionCount ?? 0;

            Debug.Log($"{McpProtocol.LogPrefix} [HTTP] Server Status:\n" +
                $"  Status: {status}\n" +
                $"  Port: {settings.HttpPort}\n" +
                $"  Startup Server: {settings.StartupServer}\n" +
                $"  Active Sessions: {sessionCount}\n" +
                $"  Session Timeout: {settings.SessionTimeoutSeconds}s\n" +
                $"  SSE Keep-Alive: {settings.SseKeepAliveIntervalSeconds}s");
        }

        /// <summary>
        /// Log MCP configuration for Streamable HTTP transport
        /// </summary>
        [MenuItem("Tools/LoopMcpServer/HTTP/Print MCP configuration to console")]
        public static void LogMcpConfiguration()
        {
            var settings = LoopMcpServerSettings.Instance;

            // Configuration for direct HTTP connection (no proxy needed)
            string template = $@"{{
  ""mcpServers"": {{
    ""loop-unity-http"": {{
        ""type"": ""http"",
        ""url"": ""http://127.0.0.1:{settings.HttpPort}{McpHttpTransport.EndpointPath}""
    }}
  }}
}}";

            Debug.Log($"{McpProtocol.LogPrefix} [HTTP] MCP Configuration (Streamable HTTP):\n{template}");
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Check if the HTTP server is running
        /// </summary>
        public static bool IsRunning => _isRunning;

        /// <summary>
        /// Get the number of active sessions
        /// </summary>
        public static int ActiveSessionCount => _sessionManager?.ActiveSessionCount ?? 0;

        /// <summary>
        /// Get the session manager (for testing/advanced usage)
        /// </summary>
        public static SessionManager SessionManager => _sessionManager;

        private static string BuildRegistrySummary()
        {
            if (_registry == null)
            {
                return "Tools: 0\nPrompts: 0\nResources: 0";
            }

            var toolNames = _registry.SyncTools.Keys.Concat(_registry.AsyncTools.Keys).OrderBy(name => name).ToList();
            var promptNames = _registry.Prompts.Keys.OrderBy(name => name).ToList();
            var resourceNames = _registry.Resources.Keys.OrderBy(name => name).ToList();

            return $"Tools: {toolNames.Count} ({string.Join(", ", toolNames)})\n" +
                   $"Prompts: {promptNames.Count} ({string.Join(", ", promptNames)})\n" +
                   $"Resources: {resourceNames.Count} ({string.Join(", ", resourceNames)})";
        }

        #endregion
    }
}
