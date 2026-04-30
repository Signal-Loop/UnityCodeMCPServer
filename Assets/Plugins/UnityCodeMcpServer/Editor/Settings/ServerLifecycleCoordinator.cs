using System;
using UnityCodeMcpServer.Servers.StreamableHttp;

namespace UnityCodeMcpServer.Settings
{
    /// <summary>
    /// Centralizes HTTP server lifecycle calls.
    /// Handlers are overridable for tests to avoid starting real listeners.
    /// </summary>
    public static class ServerLifecycleCoordinator
    {
        private static Action _startHttp = UnityCodeMcpHttpServer.StartServer;
        private static Action _stopHttp = UnityCodeMcpHttpServer.StopServer;
        private static Action _restartHttp = UnityCodeMcpHttpServer.RestartServer;

        public static void UpdateServerState()
        {
            UpdateServerState(restartHttp: false);
        }

        public static void UpdateServerState(bool restartHttp)
        {
            if (restartHttp)
            {
                _restartHttp?.Invoke();
                return;
            }

            _startHttp?.Invoke();
        }

        public static void SetHandlers(
            Action startHttp = null,
            Action stopHttp = null,
            Action restartHttp = null)
        {
            _startHttp = startHttp ?? UnityCodeMcpHttpServer.StartServer;
            _stopHttp = stopHttp ?? UnityCodeMcpHttpServer.StopServer;
            _restartHttp = restartHttp ?? UnityCodeMcpHttpServer.RestartServer;
        }

        public static void ResetHandlers()
        {
            _startHttp = UnityCodeMcpHttpServer.StartServer;
            _stopHttp = UnityCodeMcpHttpServer.StopServer;
            _restartHttp = UnityCodeMcpHttpServer.RestartServer;
        }
    }
}
