using UnityEngine;
using UnityEditor;

namespace LoopMcpServer.Settings
{
    /// <summary>
    /// ScriptableObject for configuring the MCP Server settings.
    /// Create via Assets > Create > LoopMcpServer > Server Settings
    /// </summary>
    [CreateAssetMenu(fileName = "LoopMcpServerSettings", menuName = "LoopMcpServer/Server Settings")]
    public class LoopMcpServerSettings : ScriptableObject
    {
        public enum ServerStartupMode
        {
            Stdio,
            Http
        }

        [Header("Server Selection")]
        [Tooltip("Select which server automatically starts in the Unity Editor")]
        public ServerStartupMode StartupServer = ServerStartupMode.Stdio;

        [Tooltip("Enable verbose logging for debugging")]
        public bool VerboseLogging;

        [Header("TCP Server Configuration")]
        [Tooltip("The port the TCP server will listen on")]
        public int Port = 21088;

        [Tooltip("Maximum number of pending connections in the listen queue")]
        public int Backlog = 10;

        [Tooltip("Read timeout in milliseconds (0 = infinite)")]
        public int ReadTimeoutMs = 30000;

        [Tooltip("Write timeout in milliseconds (0 = infinite)")]
        public int WriteTimeoutMs = 30000;

        [Header("Streamable HTTP Server Configuration")]
        [Tooltip("The port the HTTP server will listen on")]
        public int HttpPort = 3001;

        [Tooltip("Session timeout in seconds (0 = no timeout)")]
        public int SessionTimeoutSeconds = 3600;

        [Tooltip("Interval for SSE keep-alive pings in seconds")]
        public int SseKeepAliveIntervalSeconds = 30;

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

        private void OnValidate()
        {
            ApplySelection();
        }

        /// <summary>
        /// Start/stop servers according to the current startup selection.
        /// Exposed for tests and editor automation.
        /// </summary>
        public void ApplySelection()
        {
            ServerLifecycleCoordinator.ApplySelection(StartupServer);
        }

        /// <summary>
        /// Show the settings asset in the inspector
        /// </summary>
        [MenuItem("Tools/LoopMcpServer/Show Settings")]
        public static void ShowSettings()
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(LoopMcpServerSettings).Name}");
            LoopMcpServerSettings settings;
            if (guids.Length > 0)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                settings = AssetDatabase.LoadAssetAtPath<LoopMcpServerSettings>(assetPath);
            }
            else
            {
                // Create new settings asset in Assets/Resources
                var resourcesPath = "Assets/Resources";
                if (!System.IO.Directory.Exists(resourcesPath))
                {
                    System.IO.Directory.CreateDirectory(resourcesPath);
                }

                settings = CreateInstance<LoopMcpServerSettings>();
                var assetPath = System.IO.Path.Combine(resourcesPath, "LoopMcpServerSettings.asset");
                AssetDatabase.CreateAsset(settings, assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"{Protocol.McpProtocol.LogPrefix} Created new LoopMcpServerSettings asset at {assetPath}");
            }

            if (settings != null)
            {
                EditorGUIUtility.PingObject(settings);
                Selection.activeObject = settings;
            }
        }
    }
}
