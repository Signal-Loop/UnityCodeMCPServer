using System;
using System.Linq;
using System.Net;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityCodeMcpServer.Handlers;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Registry;
using UnityCodeMcpServer.Settings;
using UnityEditor;
using UnityEngine;

namespace UnityCodeMcpServer.Servers.StreamableHttp
{
    /// <summary>
    /// Streamable HTTP Server that handles MCP protocol connections per specification 2025-03-26.
    /// Auto-starts with Unity Editor and handles domain reloads gracefully.
    /// Runs alongside the TCP server on a separate port.
    /// </summary>
    [InitializeOnLoad]
    public static class UnityCodeMcpHttpServer
    {
        private static HttpListener _listener;
        private static CancellationTokenSource _serverCts;
        private static McpRegistry _registry;
        private static McpMessageHandler _messageHandler;
        private static HttpRequestHandler _requestHandler;

        static UnityCodeMcpHttpServer()
        {
            // Don't start server in batch mode (AssetImportWorkers, build processes, etc.)
            if (Application.isBatchMode)
            {
                return;
            }

            // Subscribe to editor events
            EditorApplication.quitting += OnEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        private static void OnEditorQuitting()
        {
            LoopLogger.Debug($"{McpProtocol.LogPrefix} [HTTP] Editor quitting");
            StopServer("editor-quitting");
        }

        private static void OnBeforeAssemblyReload()
        {
            LoopLogger.Debug($"{McpProtocol.LogPrefix} [HTTP] OnBeforeAssemblyReload event");
            StopServer("assembly-reload");
        }

        private static void OnAfterAssemblyReload()
        {
            LoopLogger.Debug($"{McpProtocol.LogPrefix} [HTTP] UnityCodeMcpHttpServer Assembly reload completed");
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
            var settings = UnityCodeMcpServerSettings.Instance;

            if (settings.StartupServer != UnityCodeMcpServerSettings.ServerStartupMode.Http)
            {
                LoopLogger.Debug($"{McpProtocol.LogPrefix} [HTTP] Startup skipped because server selection is {settings.StartupServer}");
                return;
            }

            if (_listener != null)
            {
                LoopLogger.Debug($"{McpProtocol.LogPrefix} [HTTP] Start skipped because listener already exists reason={reason}");
                return;
            }

            var prefix = $"http://127.0.0.1:{settings.HttpPort}/mcp/";

            try
            {
                // Initialize registry and handlers
                _registry = new McpRegistry();
                _registry.DiscoverAndRegisterAll();
                _messageHandler = new McpMessageHandler(_registry);
                _requestHandler = new HttpRequestHandler(_messageHandler);

                // Configure and start HTTP listener
                _serverCts = new CancellationTokenSource();
                _listener = new HttpListener();

                // Bind to localhost only (127.0.0.1) for security
                // HttpListener with trailing slash handles both /mcp and /mcp/
                _listener.Prefixes.Add(prefix);

                _listener.Start();

                LoopLogger.Info($"{McpProtocol.LogPrefix} [HTTP] Server started on {prefix}\n{BuildRegistrySummary()}");

                // Start accepting requests
                AcceptRequestsAsync(_serverCts.Token).Forget();
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 5)
            {
                // Access denied
                LoopLogger.Error($"{McpProtocol.LogPrefix} [HTTP] Access denied. Run as admin or add URL reservation:\n" +
                    $"netsh http add urlacl url=http://127.0.0.1:{settings.HttpPort}{McpHttpTransport.EndpointPath} user=Everyone");
                CleanupFailedStart();
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 183)
            {
                LoopLogger.Error($"{McpProtocol.LogPrefix} [HTTP] Port {settings.HttpPort} is already in use");
                CleanupFailedStart();
            }
            catch (HttpListenerException ex)
            {
                LoopLogger.Error($"{McpProtocol.LogPrefix} [HTTP] Failed to start server: {prefix} {ex.Message}");
                CleanupFailedStart();

            }
            catch (Exception ex)
            {
                LoopLogger.Error($"{McpProtocol.LogPrefix} [HTTP] Failed to start server: {prefix} {ex.Message}");
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
            if (_listener == null && _serverCts == null)
            {
                return;
            }

            LoopLogger.Debug($"{McpProtocol.LogPrefix} [HTTP] Stopping server reason={reason}");

            // Cancel all pending operations
            _serverCts?.Cancel();

            // Stop and close the listener
            try
            {
                _listener?.Abort();
            }
            catch (Exception ex)
            {
                LoopLogger.Warn($"{McpProtocol.LogPrefix} [HTTP] Error aborting listener: {ex.Message}");
            }

            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch (Exception ex)
            {
                LoopLogger.Warn($"{McpProtocol.LogPrefix} [HTTP] Error during listener cleanup: {ex.Message}");
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
                LoopLogger.Warn($"{McpProtocol.LogPrefix} [HTTP] Error disposing CTS: {ex.Message}");
            }
            finally
            {
                _serverCts = null;
            }

            _requestHandler = null;
            _messageHandler = null;
            _registry = null;

            LoopLogger.Debug($"{McpProtocol.LogPrefix} [HTTP] Server stopped reason={reason}");
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
                        LoopLogger.Error($"{McpProtocol.LogPrefix} [HTTP] Error accepting request: {ex.Message}");
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
                    LoopLogger.Error($"{McpProtocol.LogPrefix} [HTTP] Unhandled request error: {ex}");
                }
            }
        }

        #region Menu Items

        /// <summary>
        /// Force refresh the registry
        /// </summary>
        [MenuItem("Tools/UnityCodeMcpServer/HTTP/Refresh Registry")]
        public static void RefreshRegistry()
        {
            _registry?.DiscoverAndRegisterAll();
            LoopLogger.Info($"{McpProtocol.LogPrefix} [HTTP] Registry refreshed");
        }

        /// <summary>
        /// Restart the HTTP server
        /// </summary>
        [MenuItem("Tools/UnityCodeMcpServer/HTTP/Restart Server")]
        public static void RestartServer()
        {
            if (_listener != null)
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
            var settings = UnityCodeMcpServerSettings.Instance;

            var status = _listener != null && _listener.IsListening ? "Running" : "Stopped";

            LoopLogger.Info($"{McpProtocol.LogPrefix} [HTTP] Server Status:\n" +
                $"  Status: {status}\n" +
                $"  Port: {settings.HttpPort}\n" +
                $"  Startup Server: {settings.StartupServer}");
        }

        /// <summary>
        /// Log MCP configuration for Streamable HTTP transport
        /// </summary>
        [MenuItem("Tools/UnityCodeMcpServer/HTTP/Print MCP configuration to console")]
        public static void LogMcpConfiguration()
        {
            var settings = UnityCodeMcpServerSettings.Instance;

            // Configuration for direct HTTP connection (no proxy needed)
            string template = $@"{{
  ""mcpServers"": {{
    ""unity-code-mcp-http"": {{
        ""type"": ""http"",
        ""url"": ""http://127.0.0.1:{settings.HttpPort}{McpHttpTransport.EndpointPath}""
    }}
  }}
}}";

            Debug.Log($"{McpProtocol.LogPrefix} [HTTP] MCP Configuration (Streamable HTTP):\n{template}");
        }

        #endregion

        #region Public Properties

        private static void CleanupFailedStart()
        {
            try
            {
                _listener?.Close();
            }
            catch
            {
                // Ignore cleanup errors for failed starts.
            }
            finally
            {
                _listener = null;
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
