using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using LoopMcpServer.Handlers;
using LoopMcpServer.Protocol;
using LoopMcpServer.Registry;
using LoopMcpServer.Settings;
using UnityEditor;
using UnityEngine;

namespace LoopMcpServer.Server
{
    /// <summary>
    /// TCP Server that handles MCP protocol connections.
    /// Auto-starts with Unity Editor and handles domain reloads.
    /// </summary>
    [InitializeOnLoad]
    public static class LoopMcpTcpServer
    {
        private static TcpListener _listener;
        private static CancellationTokenSource _serverCts;
        private static McpRegistry _registry;
        private static McpMessageHandler _messageHandler;
        private static bool _isRunning;

        static LoopMcpTcpServer()
        {
            // Subscribe to editor events
            EditorApplication.quitting += OnEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

            // Start the server
            StartServer();
        }

        private static void StartServer()
        {
            if (_isRunning)
            {
                Debug.LogWarning($"{McpProtocol.LogPrefix} Server already running");
                return;
            }

            try
            {
                var settings = LoopMcpServerSettings.Instance;

                // Initialize registry and handler
                _registry = new McpRegistry();
                _registry.DiscoverAndRegisterAll();
                _messageHandler = new McpMessageHandler(_registry);

                // Start TCP listener
                _serverCts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Loopback, settings.Port);
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Start(settings.Backlog);
                _isRunning = true;

                Debug.Log($"{McpProtocol.LogPrefix} Server started on port {settings.Port}");

                // Start accepting connections
                AcceptClientsAsync(_serverCts.Token).Forget();
            }
            catch (Exception ex)
            {
                Debug.LogError($"{McpProtocol.LogPrefix} Failed to start server: {ex.Message}");
                _isRunning = false;
            }
        }

        private static void StopServer()
        {
            if (!_isRunning)
                return;

            Debug.Log($"{McpProtocol.LogPrefix} Stopping server...");

            _serverCts?.Cancel();
            _serverCts?.Dispose();
            _serverCts = null;

            try
            {
                _listener?.Server?.Close();
                _listener?.Stop();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{McpProtocol.LogPrefix} Error during listener cleanup: {ex.Message}");
            }
            finally
            {
                _listener = null;
            }

            _isRunning = false;

            Debug.Log($"{McpProtocol.LogPrefix} Server stopped");
        }

        private static void OnEditorQuitting()
        {
            StopServer();
        }

        private static void OnBeforeAssemblyReload()
        {
            StopServer();
        }

        private static async UniTaskVoid AcceptClientsAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();

                    if (LoopMcpServerSettings.Instance.VerboseLogging)
                    {
                        Debug.Log($"{McpProtocol.LogPrefix} Client connected from {client.Client.RemoteEndPoint}");
                    }

                    HandleClientAsync(client, ct).Forget();
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
                        Debug.LogError($"{McpProtocol.LogPrefix} Error accepting client: {ex.Message}");
                    }
                }
            }
        }

        private static async UniTaskVoid HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            var settings = LoopMcpServerSettings.Instance;

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
                            Debug.LogWarning($"{McpProtocol.LogPrefix} Invalid message length: {messageLength}");
                            break;
                        }

                        // Read message body
                        var messageBuffer = new byte[messageLength];
                        bytesRead = await ReadExactAsync(stream, messageBuffer, 0, messageLength, ct);
                        if (bytesRead < messageLength)
                        {
                            Debug.LogWarning($"{McpProtocol.LogPrefix} Incomplete message received");
                            break;
                        }

                        var message = Encoding.UTF8.GetString(messageBuffer);

                        if (settings.VerboseLogging)
                        {
                            Debug.Log($"{McpProtocol.LogPrefix} Received: {message}");
                        }

                        // Process message on main thread to access Unity APIs
                        await UniTask.SwitchToMainThread();
                        var response = await _messageHandler.ProcessMessageAsync(message);

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

                            if (settings.VerboseLogging)
                            {
                                Debug.Log($"{McpProtocol.LogPrefix} Sent: {response}");
                            }
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
                    Debug.LogError($"{McpProtocol.LogPrefix} Client handler error: {ex.Message}");
                }
            }
            finally
            {
                if (settings.VerboseLogging)
                {
                    Debug.Log($"{McpProtocol.LogPrefix} Client disconnected");
                }
            }
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
        /// Force refresh the registry (useful after adding new tools/prompts/resources)
        /// </summary>
        [MenuItem("Tools/LoopMcpServer/Refresh Registry")]
        public static void RefreshRegistry()
        {
            _registry?.DiscoverAndRegisterAll();
            Debug.Log($"{McpProtocol.LogPrefix} Registry refreshed");
        }

        /// <summary>
        /// Restart the server
        /// </summary>
        [MenuItem("Tools/LoopMcpServer/Restart Server")]
        public static void RestartServer()
        {
            RestartServerAsync().Forget();
        }

        /// <summary>
        /// Log MCP configuration to console
        /// </summary>
        [MenuItem("Tools/LoopMcpServer/Log MCP Configuration")]
        public static void LogMcpConfiguration()
        {
            var settings = LoopMcpServerSettings.Instance;
            string pathToStdio = System.IO.Path.GetFullPath("Assets/Plugins/Loop4UnityMcpServer/Editor/STDIO~").Replace("\\", "/");
            string template = $@"{{
  ""mcpServers"": {{
    ""unity"": {{
      ""command"": ""uv"",
      ""args"": [
        ""run"",
        ""--directory"",
        ""{pathToStdio}"",
        ""loop-mcp-stdio"",
        ""--host"",
        ""localhost"",
        ""--port"",
        ""{settings.Port}""
      ]
    }}
  }}
}}";
            Debug.Log($"{McpProtocol.LogPrefix} MCP Configuration:\n{template}");
        }

        private static async UniTaskVoid RestartServerAsync()
        {
            StopServer();
            // Wait for socket to be fully released
            await UniTask.Delay(100);
            StartServer();
        }

        /// <summary>
        /// Check if server is running
        /// </summary>
        public static bool IsRunning => _isRunning;
    }
}
