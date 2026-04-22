using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
        private const int StartupRetryCount = 3;
        private const int StartupRetryDelayMs = 50;

        private static LoopbackHttpServerTransport _transport;
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

            if (settings.StartupServer != UnityCodeMcpServerSettings.ServerStartupMode.Http)
            {
                UnityCodeMcpServerLogger.Debug($"[UnityCodeMcpHttpServer] Startup skipped because server selection is {settings.StartupServer}");
                return;
            }

            if (_transport != null)
            {
                UnityCodeMcpServerLogger.Debug($"[UnityCodeMcpHttpServer] Start skipped because transport already exists reason={reason}");
                return;
            }

            string prefix = $"http://127.0.0.1:{settings.HttpPort}/mcp/";

            try
            {
                // Initialize registry and handlers
                _registry = new McpRegistry();
                _registry.DiscoverAndRegisterAll();
                _messageHandler = new McpMessageHandler(_registry);
                _requestHandler = new HttpRequestHandler(_messageHandler);

                _serverCts = new CancellationTokenSource();
                _transport = StartTransportWithRetry(settings.HttpPort, settings.Backlog);

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

            UnityCodeMcpServerLogger.Info($"[UnityCodeMcpHttpServer] Server Status:\n" +
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
                        Math.Max(1, backlog));
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

        private static async UniTask HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            NetworkStream stream = client.GetStream();

            try
            {
                LoopbackHttpRequest request = await LoopbackHttpProtocol.ReadRequestAsync(stream, client.Client.RemoteEndPoint, ct);
                if (request == null)
                {
                    return;
                }

                MemoryStream responseBuffer = new();
                LoopbackHttpContext context = new(
                    request,
                    new LoopbackHttpResponse(responseBuffer));

                await _requestHandler.HandleRequestAsync(context, ct);
                await LoopbackHttpProtocol.WriteResponseAsync(stream, context.Response, responseBuffer.ToArray(), ct);
            }
            catch (InvalidDataException ex)
            {
                await WritePlainTextResponseAsync(stream, 400, ex.Message, ct);
            }
            catch (Exception ex)
            {
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
