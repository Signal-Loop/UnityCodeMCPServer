using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// Default assemblies that are always included and cannot be removed.
        /// These are the core assemblies required for script execution.
        /// </summary>
        public static readonly string[] DefaultAssemblyNames =
        {
            "System.Core",
            "UnityEngine.CoreModule",
            "UnityEditor.CoreModule",
            "Assembly-CSharp",
            "Assembly-CSharp-Editor",
            // "Unity.InputSystem",
            // "System.Text.Json",
            // "UnityEngine.Physics2DModule",
            // "UnityEngine.TextRenderingModule",
            // "UnityEngine.UI",
            // "UnityEngine.UIElementsModule",
            // "UnityEngine.UIModule",
            // "UnityEngine.TestRunner",
            // "UnityEditor.TestRunner"
        };

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

        [Header("Script Execution Assemblies")]
        [Tooltip("Additional assemblies to load for C# script execution (beyond default assemblies)")]
        public List<string> AdditionalAssemblyNames = new List<string>();

        /// <summary>
        /// Get all assembly names to be loaded for script execution (default + additional)
        /// </summary>
        public string[] GetAllAssemblyNames()
        {
            if (AdditionalAssemblyNames == null || AdditionalAssemblyNames.Count == 0)
            {
                return DefaultAssemblyNames;
            }

            var allAssemblies = new List<string>(DefaultAssemblyNames);
            foreach (var assemblyName in AdditionalAssemblyNames)
            {
                if (!string.IsNullOrWhiteSpace(assemblyName) && !allAssemblies.Contains(assemblyName))
                {
                    allAssemblies.Add(assemblyName);
                }
            }

            return allAssemblies.ToArray();
        }

        /// <summary>
        /// Add an assembly to the additional assemblies list if not already present
        /// </summary>
        public bool AddAssembly(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                return false;
            }

            if (DefaultAssemblyNames.Contains(assemblyName))
            {
                return false; // Cannot add default assemblies
            }

            if (AdditionalAssemblyNames == null)
            {
                AdditionalAssemblyNames = new List<string>();
            }

            if (AdditionalAssemblyNames.Contains(assemblyName))
            {
                return false; // Already exists
            }

            AdditionalAssemblyNames.Add(assemblyName);
            EditorUtility.SetDirty(this);
            return true;
        }

        /// <summary>
        /// Remove an assembly from the additional assemblies list
        /// </summary>
        public bool RemoveAssembly(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName) || AdditionalAssemblyNames == null)
            {
                return false;
            }

            var removed = AdditionalAssemblyNames.Remove(assemblyName);
            if (removed)
            {
                EditorUtility.SetDirty(this);
            }

            return removed;
        }

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
