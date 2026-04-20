using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityCodeMcpServer.Handlers;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Registry;
using UnityCodeMcpServer.Settings;
using UnityEditor;
using UnityEngine;

namespace UnityCodeMcpServer.Servers.Tcp
{
    /// <summary>
    /// TCP Server that handles MCP protocol connections.
    /// Auto-starts with Unity Editor and handles domain reloads.
    /// </summary>
    [InitializeOnLoad]
    public static class UnityCodeMcpTcpServer
    {
        private static TcpListener _listener;
        private static CancellationTokenSource _serverCts;
        private static McpRegistry _registry;
        private static McpMessageHandler _messageHandler;
        private static bool _isRunning;
        private static int _activeClientCount;

        /// <summary>
        /// Number of TCP clients currently connected to the server.
        /// </summary>
        public static int ActiveClientCount => _activeClientCount;

        // Pre-compiled regex for fast ping detection without JSON parsing or main-thread switch.
        private static readonly Regex PingMethodRegex = new Regex(
            @"""method""\s*:\s*""ping""", RegexOptions.Compiled);
        private static readonly Regex IdRegex = new Regex(
            @"""id""\s*:\s*(""[^""]*""|\d+|null)", RegexOptions.Compiled);

        static UnityCodeMcpTcpServer()
        {
            // Don't start server in batch mode (AssetImportWorkers, build processes, etc.)
            if (Application.isBatchMode)
            {
                return;
            }

            // Subscribe to editor events
            EditorApplication.quitting += OnEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

            // Delay server start until editor is fully ready.
            // Starting immediately in [InitializeOnLoad] causes UniTask.SwitchToMainThread()
            // to hang because the PlayerLoop isn't pumping yet after domain reload.
            EditorApplication.delayCall += StartServer;
        }

        public static void StartServer()
        {
            var settings = UnityCodeMcpServerSettings.Instance;

            if (_isRunning)
            {
                LoopLogger.Debug($"{McpProtocol.LogPrefix} [STDIO] Server already running");
                return;
            }

            try
            {

                if (settings.StartupServer != UnityCodeMcpServerSettings.ServerStartupMode.Stdio)
                {
                    LoopLogger.Debug($"{McpProtocol.LogPrefix} [STDIO] Startup skipped because server selection is {settings.StartupServer}");
                    return;
                }

                // Initialize registry and handler
                _registry = new McpRegistry();
                _registry.DiscoverAndRegisterAll();
                _messageHandler = new McpMessageHandler(_registry);

                // Start TCP listener
                _serverCts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Loopback, settings.StdioPort);
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                // Set linger option to force immediate socket close on shutdown
                _listener.Server.LingerState = new LingerOption(true, 0);
                _listener.Start(settings.Backlog);
                _isRunning = true;

                LoopLogger.Info($"{McpProtocol.LogPrefix} [STDIO] Server started on port {settings.StdioPort}\n{BuildRegistrySummary()}");

                // Start accepting connections
                AcceptClientsAsync(_serverCts.Token).Forget();
            }
            catch (Exception ex)
            {
                LoopLogger.Error($"{McpProtocol.LogPrefix} [STDIO] Failed to start server: {ex.Message}");
                _isRunning = false;
            }
        }

        public static void StopServer()
        {
            StopServerInternal("requested");
        }

        public static void StopServer(string reason)
        {
            StopServerInternal(reason);
        }

        private static void StopServerInternal(string reason)
        {
            if (!_isRunning)
                return;

            var clientCount = _activeClientCount;
            LoopLogger.Info($"{McpProtocol.LogPrefix} [STDIO] Stopping server reason={reason} active_clients={clientCount}");

            _serverCts?.Cancel();
            _serverCts?.Dispose();
            _serverCts = null;

            try
            {
                // Explicitly dispose the underlying socket first
                if (_listener?.Server != null)
                {
                    _listener.Server.Close(0); // Force immediate close
                    _listener.Server.Dispose();
                }
                _listener?.Stop();
            }
            catch (Exception ex)
            {
                LoopLogger.Warn($"{McpProtocol.LogPrefix} [STDIO] Error during listener cleanup: {ex.Message}");
            }
            finally
            {
                _listener = null;
            }

            _isRunning = false;

            LoopLogger.Info($"{McpProtocol.LogPrefix} [STDIO] Server stopped reason={reason}");
        }

        private static void OnEditorQuitting()
        {
            StopServer("editor-quitting");
        }

        private static void OnBeforeAssemblyReload()
        {
            StopServer("assembly-reload");
        }

        private static async UniTaskVoid AcceptClientsAsync(CancellationToken ct)
        {
            // Capture settings on main thread before switching to thread pool.
            var settings = UnityCodeMcpServerSettings.Instance;

            // Run accept loop on the thread pool so that connection handling
            // (especially health-probe pings) is not blocked when the Unity
            // main thread is busy after domain reload.
            await UniTask.SwitchToThreadPool();

            while (!ct.IsCancellationRequested)
            {
                var listener = _listener;
                if (listener == null) break;

                try
                {
                    var client = await listener.AcceptTcpClientAsync();
                    var endpoint = client.Client.RemoteEndPoint;
                    var clientCount = Interlocked.Increment(ref _activeClientCount);

                    LoopLogger.Info($"{McpProtocol.LogPrefix} [STDIO] Client connected from {endpoint} active_clients={clientCount}");

                    HandleClientAsync(client, settings, ct).Forget();
                }
                catch (ObjectDisposedException)
                {
                    // Listener was stopped
                    break;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
                {
                    // Listener was stopped
                    break;
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        LoopLogger.Error($"{McpProtocol.LogPrefix} [STDIO] Error accepting client: {ex.Message}");
                    }
                }
            }
        }

        private static async UniTaskVoid HandleClientAsync(
            TcpClient client, UnityCodeMcpServerSettings settings, CancellationToken ct)
        {
            // This method runs entirely on the thread pool (from AcceptClientsAsync).
            // Only non-ping message processing switches to the main thread.
            var connectionTimer = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                using (client)
                {
                    client.ReceiveTimeout = settings.ReadTimeoutMs;
                    client.SendTimeout = settings.WriteTimeoutMs;

                    var stream = client.GetStream();
                    var buffer = new byte[4];

                    while (!ct.IsCancellationRequested && client.Connected)
                    {
                        // Read length prefix (4 bytes, big-endian)
                        var bytesRead = await ReadExactAsync(stream, buffer, 0, 4, ct);
                        if (bytesRead < 4)
                        {
                            // Client disconnected
                            break;
                        }

                        var messageLength = (buffer[0] << 24) | (buffer[1] << 16) | (buffer[2] << 8) | buffer[3];

                        if (messageLength <= 0 || messageLength > 10 * 1024 * 1024) // Max 10MB
                        {
                            LoopLogger.Warn($"{McpProtocol.LogPrefix} [STDIO] Invalid message length: {messageLength}");
                            break;
                        }

                        // Read message body
                        var messageBuffer = new byte[messageLength];
                        bytesRead = await ReadExactAsync(stream, messageBuffer, 0, messageLength, ct);
                        if (bytesRead < messageLength)
                        {
                            LoopLogger.Warn($"{McpProtocol.LogPrefix} [STDIO] Incomplete message received");
                            break;
                        }

                        var message = Encoding.UTF8.GetString(messageBuffer);

                        LoopLogger.Trace($"{McpProtocol.LogPrefix} [STDIO] Received: {message}");

                        // Ping is handled entirely on thread pool (no main-thread
                        // dependency). All other messages require the main thread for
                        // Unity API access.
                        string response;
                        if (TryBuildPingResponse(message, out var pingResponse))
                        {
                            response = pingResponse;
                        }
                        else
                        {
                            // Process message on main thread to access Unity APIs
                            await UniTask.SwitchToMainThread(ct);
                            response = await _messageHandler.ProcessMessageAsync(message);
                            // Return to thread pool for I/O
                            await UniTask.SwitchToThreadPool();
                        }

                        if (response != null)
                        {
                            // Send response with length prefix
                            var responseBytes = Encoding.UTF8.GetBytes(response);
                            var lengthPrefix = new byte[4];
                            lengthPrefix[0] = (byte)((responseBytes.Length >> 24) & 0xFF);
                            lengthPrefix[1] = (byte)((responseBytes.Length >> 16) & 0xFF);
                            lengthPrefix[2] = (byte)((responseBytes.Length >> 8) & 0xFF);
                            lengthPrefix[3] = (byte)(responseBytes.Length & 0xFF);

                            await stream.WriteAsync(lengthPrefix, 0, 4, ct);
                            await stream.WriteAsync(responseBytes, 0, responseBytes.Length, ct);
                            await stream.FlushAsync(ct);

                            LoopLogger.Trace($"{McpProtocol.LogPrefix} [STDIO] Sent: {response}");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when server is stopping
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    if (IsExpectedClientDisconnect(ex))
                    {
                        LoopLogger.Trace($"{McpProtocol.LogPrefix} [STDIO] Client handler disconnected: {ex.Message}");
                    }
                    else
                    {
                        LoopLogger.Error($"{McpProtocol.LogPrefix} [STDIO] Client handler error: {ex.Message}");
                    }
                }
            }
            finally
            {
                connectionTimer.Stop();
                var remainingClients = Interlocked.Decrement(ref _activeClientCount);
                LoopLogger.Info($"{McpProtocol.LogPrefix} [STDIO] Client disconnected duration_ms={connectionTimer.ElapsedMilliseconds} active_clients={remainingClients}");
            }
        }

        private static bool IsExpectedClientDisconnect(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is ObjectDisposedException)
                {
                    return true;
                }

                if (current is IOException && current.InnerException is null)
                {
                    continue;
                }

                if (current is SocketException socketException)
                {
                    switch (socketException.SocketErrorCode)
                    {
                        case SocketError.ConnectionAborted:
                        case SocketError.ConnectionReset:
                        case SocketError.Shutdown:
                        case SocketError.OperationAborted:
                        case SocketError.Interrupted:
                            return true;
                    }
                }
            }

            return false;
        }

        private static async UniTask<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            var totalRead = 0;
            while (totalRead < count)
            {
                var bytesRead = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, ct);
                if (bytesRead == 0)
                {
                    // Connection closed
                    return totalRead;
                }
                totalRead += bytesRead;
            }
            return totalRead;
        }

        /// <summary>
        /// Build a JSON-RPC ping response without JSON parsing or main-thread access.
        /// Uses regex to detect method=="ping" and extract the request id.
        /// Returns false for any non-ping message, letting normal processing handle it.
        /// </summary>
        private static bool TryBuildPingResponse(string message, out string response)
        {
            response = null;

            if (!PingMethodRegex.IsMatch(message))
                return false;

            var idMatch = IdRegex.Match(message);
            var idValue = idMatch.Success ? idMatch.Groups[1].Value : "null";

            response = $"{{\"jsonrpc\":\"2.0\",\"id\":{idValue},\"result\":{{}}}}";
            return true;
        }

        /// <summary>
        /// Force refresh the registry (useful after adding new tools/prompts/resources)
        /// </summary>
        [MenuItem("Tools/UnityCodeMcpServer/STDIO/Refresh Registry")]
        public static void RefreshRegistry()
        {
            _registry?.DiscoverAndRegisterAll();
            LoopLogger.Info($"{McpProtocol.LogPrefix} [STDIO] Registry refreshed");
        }

        /// <summary>
        /// Restart the server
        /// </summary>
        [MenuItem("Tools/UnityCodeMcpServer/STDIO/Restart Server")]
        public static void RestartServer()
        {
            RestartServerAsync().Forget();
        }

        /// <summary>
        /// Log MCP configuration to console
        /// </summary>
        [MenuItem("Tools/UnityCodeMcpServer/STDIO/Print MCP configuration to console")]
        public static void LogMcpConfiguration()
        {
            string pathToStdio = System.IO.Path.GetFullPath("Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~").Replace("\\", "/");

            string template = $@"{{
  ""servers"": {{
    ""unity-code-mcp-stdio"": {{
      ""command"": ""uv"",
      ""args"": [
        ""run"",
        ""--directory"",
        ""{pathToStdio}"",
        ""unity-code-mcp-stdio""
      ]
    }}
  }}
}}";
            Debug.Log($"{McpProtocol.LogPrefix} [STDIO] MCP Configuration:\n{template}");
        }

        private static async UniTaskVoid RestartServerAsync()
        {
            StopServer();
            // Wait for socket to be fully released (increased from 100ms to 500ms)
            // Windows may keep socket in TIME_WAIT state; this ensures proper cleanup
            await UniTask.Delay(500);
            StartServer();
        }

        /// <summary>
        /// Check if server is running
        /// </summary>
        public static bool IsRunning => _isRunning;

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
    }
}
