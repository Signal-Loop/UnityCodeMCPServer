using System;
using LoopMcpServer.Servers.StreamableHttp;
using LoopMcpServer.Servers.Tcp;

namespace LoopMcpServer.Settings
{
    /// <summary>
    /// Centralizes which transport should be running based on the configured startup mode.
    /// Handlers are overridable for tests to avoid starting real listeners.
    /// </summary>
    public static class ServerLifecycleCoordinator
    {
        private static Action _startTcp = LoopMcpTcpServer.StartServer;
        private static Action _stopTcp = LoopMcpTcpServer.StopServer;
        private static Action _startHttp = LoopMcpHttpServer.StartServer;
        private static Action _stopHttp = LoopMcpHttpServer.StopServer;

        public static void ApplySelection(LoopMcpServerSettings.ServerStartupMode mode)
        {
            switch (mode)
            {
                case LoopMcpServerSettings.ServerStartupMode.Stdio:
                    _stopHttp?.Invoke();
                    _startTcp?.Invoke();
                    break;
                case LoopMcpServerSettings.ServerStartupMode.Http:
                    _stopTcp?.Invoke();
                    _startHttp?.Invoke();
                    break;
                default:
                    _stopTcp?.Invoke();
                    _stopHttp?.Invoke();
                    break;
            }
        }

        public static void SetHandlers(
            Action startTcp = null,
            Action stopTcp = null,
            Action startHttp = null,
            Action stopHttp = null)
        {
            _startTcp = startTcp ?? LoopMcpTcpServer.StartServer;
            _stopTcp = stopTcp ?? LoopMcpTcpServer.StopServer;
            _startHttp = startHttp ?? LoopMcpHttpServer.StartServer;
            _stopHttp = stopHttp ?? LoopMcpHttpServer.StopServer;
        }

        public static void ResetHandlers()
        {
            _startTcp = LoopMcpTcpServer.StartServer;
            _stopTcp = LoopMcpTcpServer.StopServer;
            _startHttp = LoopMcpHttpServer.StartServer;
            _stopHttp = LoopMcpHttpServer.StopServer;
        }
    }
}
