using System;
using UnityCodeMcpServer.FileServer;

namespace UnityCodeMcpServer.Settings
{
    /// <summary>
    /// Centralizes file server lifecycle calls.
    /// Handlers are overridable for tests to avoid starting the real watcher.
    /// </summary>
    public static class ServerLifecycleCoordinator
    {
        private static Action _startServer = UnityCodeMcpFileServer.StartServer;
        private static Action _restartServer = RestartFileServer;

        public static void UpdateServerState()
        {
            UpdateServerState(restartServer: false);
        }

        public static void UpdateServerState(bool restartServer)
        {
            if (restartServer)
            {
                _restartServer?.Invoke();
                return;
            }

            _startServer?.Invoke();
        }

        public static void SetHandlers(
            Action startServer = null,
            Action restartServer = null)
        {
            _startServer = startServer ?? UnityCodeMcpFileServer.StartServer;
            _restartServer = restartServer ?? RestartFileServer;
        }

        public static void ResetHandlers()
        {
            _startServer = UnityCodeMcpFileServer.StartServer;
            _restartServer = RestartFileServer;
        }

        private static void RestartFileServer()
        {
            UnityCodeMcpFileServer.StopServer();
            UnityCodeMcpFileServer.StartServer();
        }
    }
}
