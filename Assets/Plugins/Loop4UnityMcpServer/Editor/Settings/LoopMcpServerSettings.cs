using UnityEngine;

namespace LoopMcpServer.Settings
{
    /// <summary>
    /// ScriptableObject for configuring the MCP Server settings.
    /// Create via Assets > Create > LoopMcpServer > Server Settings
    /// </summary>
    [CreateAssetMenu(fileName = "LoopMcpServerSettings", menuName = "LoopMcpServer/Server Settings")]
    public class LoopMcpServerSettings : ScriptableObject
    {
        [Header("TCP Server Configuration")]
        [Tooltip("The port the TCP server will listen on")]
        [SerializeField]
        private int _port = 21088;

        [Tooltip("Maximum number of pending connections in the listen queue")]
        [SerializeField]
        private int _backlog = 10;

        [Tooltip("Read timeout in milliseconds (0 = infinite)")]
        [SerializeField]
        private int _readTimeoutMs = 30000;

        [Tooltip("Write timeout in milliseconds (0 = infinite)")]
        [SerializeField]
        private int _writeTimeoutMs = 30000;

        [Tooltip("Enable verbose logging for debugging")]
        [SerializeField]
        private bool _verboseLogging;

        [Header("Streamable HTTP Server Configuration")]
        [Tooltip("The port the HTTP server will listen on")]
        [SerializeField]
        private int _httpPort = 3001;

        [Tooltip("Enable the Streamable HTTP server")]
        [SerializeField]
        private bool _enableHttpServer = true;

        [Tooltip("Session timeout in seconds (0 = no timeout)")]
        [SerializeField]
        private int _sessionTimeoutSeconds = 3600;

        [Tooltip("Interval for SSE keep-alive pings in seconds")]
        [SerializeField]
        private int _sseKeepAliveIntervalSeconds = 30;

        public int Port => _port;
        public int Backlog => _backlog;
        public int ReadTimeoutMs => _readTimeoutMs;
        public int WriteTimeoutMs => _writeTimeoutMs;
        public bool VerboseLogging => _verboseLogging;

        // HTTP Server properties
        public int HttpPort => _httpPort;
        public bool EnableHttpServer => _enableHttpServer;
        public int SessionTimeoutSeconds => _sessionTimeoutSeconds;
        public int SseKeepAliveIntervalSeconds => _sseKeepAliveIntervalSeconds;

        /// <summary>
        /// Get the singleton instance, always loading fresh from Resources to pick up changes
        /// </summary>
        public static LoopMcpServerSettings Instance
        {
            get
            {
                var instance = UnityEngine.Resources.Load<LoopMcpServerSettings>("LoopMcpServerSettings");

                if (instance == null)
                {
                    Debug.Log($"{Protocol.McpProtocol.LogPrefix} No settings found in Resources, using defaults");
                    instance = CreateInstance<LoopMcpServerSettings>();
                }

                return instance;
            }
        }
    }
}
