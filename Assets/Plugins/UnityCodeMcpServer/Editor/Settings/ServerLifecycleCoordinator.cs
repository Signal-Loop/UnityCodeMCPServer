using System;
using UnityCodeMcpServer.Servers.StreamableHttp;
using UnityCodeMcpServer.Servers.Tcp;

namespace UnityCodeMcpServer.Settings
{
    /// <summary>
    /// Centralizes which transport should be running based on the configured startup mode.
    /// Handlers are overridable for tests to avoid starting real listeners.
    /// </summary>
    public static class ServerLifecycleCoordinator
    {
        private static Action _startTcp = UnityCodeMcpTcpServer.StartServer;
        private static Action _stopTcp = UnityCodeMcpTcpServer.StopServer;
        private static Action _startHttp = UnityCodeMcpHttpServer.StartServer;
        private static Action _stopHttp = UnityCodeMcpHttpServer.StopServer;

        public static void ApplySelection(UnityCodeMcpServerSettings.ServerStartupMode mode)
        {
            switch (mode)
            {
                case UnityCodeMcpServerSettings.ServerStartupMode.Stdio:
                    _stopHttp?.Invoke();
                    _startTcp?.Invoke();
                    break;
                case UnityCodeMcpServerSettings.ServerStartupMode.Http:
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
            _startTcp = startTcp ?? UnityCodeMcpTcpServer.StartServer;
            _stopTcp = stopTcp ?? UnityCodeMcpTcpServer.StopServer;
            _startHttp = startHttp ?? UnityCodeMcpHttpServer.StartServer;
            _stopHttp = stopHttp ?? UnityCodeMcpHttpServer.StopServer;
        }

        public static void ResetHandlers()
        {
            _startTcp = UnityCodeMcpTcpServer.StartServer;
            _stopTcp = UnityCodeMcpTcpServer.StopServer;
            _startHttp = UnityCodeMcpHttpServer.StartServer;
            _stopHttp = UnityCodeMcpHttpServer.StopServer;
        }
    }
}
