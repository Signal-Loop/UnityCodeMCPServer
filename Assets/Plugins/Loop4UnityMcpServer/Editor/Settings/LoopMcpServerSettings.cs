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

        [Header("Timeouts")]
        [Tooltip("Read timeout in milliseconds (0 = infinite)")]
        [SerializeField]
        private int _readTimeoutMs = 30000;

        [Tooltip("Write timeout in milliseconds (0 = infinite)")]
        [SerializeField]
        private int _writeTimeoutMs = 30000;

        [Header("Logging")]
        [Tooltip("Enable verbose logging for debugging")]
        [SerializeField]
        private bool _verboseLogging;

        public int Port => _port;
        public int Backlog => _backlog;
        public int ReadTimeoutMs => _readTimeoutMs;
        public int WriteTimeoutMs => _writeTimeoutMs;
        public bool VerboseLogging => _verboseLogging;

        /// <summary>
        /// Get the singleton instance, always loading fresh from Resources to pick up changes
        /// </summary>
        public static LoopMcpServerSettings Instance
        {
            get
            {
                var instance = Resources.Load<LoopMcpServerSettings>("LoopMcpServerSettings");

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
