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
        private static Action _restartTcp = UnityCodeMcpTcpServer.RestartServer;
        private static Action _startHttp = UnityCodeMcpHttpServer.StartServer;
        private static Action _stopHttp = UnityCodeMcpHttpServer.StopServer;
        private static Action _restartHttp = UnityCodeMcpHttpServer.RestartServer;

        public static void UpdateServerState(UnityCodeMcpServerSettings.ServerStartupMode mode)
        {
            UpdateServerState(mode, restartTcp: false, restartHttp: false);
        }

        public static void UpdateServerState(
            UnityCodeMcpServerSettings.ServerStartupMode mode,
            bool restartTcp,
            bool restartHttp)
        {
            switch (mode)
            {
                case UnityCodeMcpServerSettings.ServerStartupMode.Stdio:
                    _stopHttp?.Invoke();
                    if (restartTcp)
                    {
                        _restartTcp?.Invoke();
                    }
                    else
                    {
                        _startTcp?.Invoke();
                    }
                    break;
                case UnityCodeMcpServerSettings.ServerStartupMode.Http:
                    _stopTcp?.Invoke();
                    if (restartHttp)
                    {
                        _restartHttp?.Invoke();
                    }
                    else
                    {
                        _startHttp?.Invoke();
                    }
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
            Action restartTcp = null,
            Action startHttp = null,
            Action stopHttp = null,
            Action restartHttp = null)
        {
            _startTcp = startTcp ?? UnityCodeMcpTcpServer.StartServer;
            _stopTcp = stopTcp ?? UnityCodeMcpTcpServer.StopServer;
            _restartTcp = restartTcp ?? UnityCodeMcpTcpServer.RestartServer;
            _startHttp = startHttp ?? UnityCodeMcpHttpServer.StartServer;
            _stopHttp = stopHttp ?? UnityCodeMcpHttpServer.StopServer;
            _restartHttp = restartHttp ?? UnityCodeMcpHttpServer.RestartServer;
        }

        public static void ResetHandlers()
        {
            _startTcp = UnityCodeMcpTcpServer.StartServer;
            _stopTcp = UnityCodeMcpTcpServer.StopServer;
            _restartTcp = UnityCodeMcpTcpServer.RestartServer;
            _startHttp = UnityCodeMcpHttpServer.StartServer;
            _stopHttp = UnityCodeMcpHttpServer.StopServer;
            _restartHttp = UnityCodeMcpHttpServer.RestartServer;
        }
    }
}
