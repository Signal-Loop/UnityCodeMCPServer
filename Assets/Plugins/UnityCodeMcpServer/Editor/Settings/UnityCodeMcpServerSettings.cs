using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityCodeMcpServer.Settings
{
    /// <summary>
    /// ScriptableObject for configuring the MCP Server settings.
    /// </summary>
    public class UnityCodeMcpServerSettings : ScriptableObject
    {
        private const string _defaultSettingsAssetPath = "Assets/Plugins/UnityCodeMcpServer/Editor/UnityCodeMcpServerSettings.asset";
        private static string _settingsAssetPath = _defaultSettingsAssetPath;

        /// <summary>
        /// Override the settings asset path for testing purposes.
        /// </summary>
        public static void SetAssetPathForTesting(string path)
        {
            _settingsAssetPath = path;
        }

        /// <summary>
        /// Reset the settings asset path to default.
        /// </summary>
        public static void ResetAssetPath()
        {
            _settingsAssetPath = _defaultSettingsAssetPath;
        }
        public enum SkillInstallTarget
        {
            GitHub,
            Claude,
            Agents,
            Custom
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
            "Assembly-CSharp-Editor"
        };

        [Header("Logging")]
        [Tooltip("Minimum log level. Messages below this level are suppressed.")]
        public Helpers.UnityCodeMcpServerLogger.LogLevel MinLogLevel = Helpers.UnityCodeMcpServerLogger.LogLevel.Info;

        [Tooltip("Enable logging to file (UnityCodeMcpServerLog.log in project root)")]
        public bool LogToFile = false;

        [Header("Script Execution Assemblies")]
        [Tooltip("Additional assemblies to load for C# script execution (beyond default assemblies)")]
        public List<string> AdditionalAssemblyNames = new();

        [Header("Input Actions")]
        [Tooltip("AssetDatabase path to the InputActionAsset used by play_unity_game")]
        public string InputActionsAssetPath = string.Empty;

        [Header("Skills Installer")]
        [HideInInspector]
        public SkillInstallTarget SkillsInstallTarget = SkillInstallTarget.Agents;

        [Tooltip("Target directory for skill file installation (persists across sessions)")]
        public string SkillsTargetPath = ".agents/skills/";

        [SerializeField, HideInInspector]
        private bool _hasInitializedSkillsInstallTarget;

        private static UnityCodeMcpServerSettings _instance;

        /// <summary>
        /// Get all assembly names to be loaded for script execution (default + additional)
        /// </summary>
        public string[] GetAllAssemblyNames()
        {
            if (AdditionalAssemblyNames == null || AdditionalAssemblyNames.Count == 0)
            {
                return DefaultAssemblyNames;
            }

            List<string> allAssemblies = new(DefaultAssemblyNames);
            foreach (string assemblyName in AdditionalAssemblyNames)
            {
                if (!string.IsNullOrWhiteSpace(assemblyName) && !allAssemblies.Contains(assemblyName))
                {
                    allAssemblies.Add(assemblyName);
                }
            }

            return allAssemblies.ToArray();
        }

        public void InitializeSkillsTarget()
        {
            if (_hasInitializedSkillsInstallTarget)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(SkillsTargetPath))
            {
                SkillsTargetPath = NormalizePath(SkillsTargetPath);

                if (IsAbsolutePath(SkillsTargetPath))
                {
                    SkillsInstallTarget = SkillInstallTarget.Custom;
                }
                else if (SkillsInstallTarget != SkillInstallTarget.Custom)
                {
                    SkillsTargetPath = GetStoredSkillsTargetPath(SkillsInstallTarget);
                }
            }
            else if (SkillsInstallTarget == SkillInstallTarget.Custom)
            {
                SkillsTargetPath = GetDefaultCustomSkillsTargetPath();
            }
            else
            {
                SkillsTargetPath = GetStoredSkillsTargetPath(SkillsInstallTarget);
            }

            _hasInitializedSkillsInstallTarget = true;
            EditorUtility.SetDirty(this);
        }

        public void SetSkillsInstallTarget(SkillInstallTarget target)
        {
            InitializeSkillsTarget();

            string currentCustomPath = SkillsInstallTarget == SkillInstallTarget.Custom
                ? NormalizePath(SkillsTargetPath)
                : string.Empty;
            string currentResolvedPath = GetEffectiveSkillsTargetPath();

            SkillsInstallTarget = target;
            if (target == SkillInstallTarget.Custom)
            {
                if (!string.IsNullOrWhiteSpace(currentCustomPath))
                {
                    SkillsTargetPath = currentCustomPath;
                }
                else
                {
                    SkillsTargetPath = NormalizePath(currentResolvedPath);
                }
            }
            else
            {
                SkillsTargetPath = GetStoredSkillsTargetPath(target);
            }

            _hasInitializedSkillsInstallTarget = true;
            EditorUtility.SetDirty(this);
        }

        public void SetCustomSkillsTargetPath(string path)
        {
            SkillsInstallTarget = SkillInstallTarget.Custom;
            SkillsTargetPath = string.IsNullOrWhiteSpace(path)
                ? GetDefaultCustomSkillsTargetPath()
                : NormalizePath(path);
            _hasInitializedSkillsInstallTarget = true;
            EditorUtility.SetDirty(this);
        }

        public string GetEffectiveSkillsTargetPath()
        {
            InitializeSkillsTarget();

            return SkillsInstallTarget == SkillInstallTarget.Custom
                ? NormalizePath(SkillsTargetPath)
                : ResolveSkillsTargetPath(SkillsInstallTarget);
        }

        public static string ResolveSkillsTargetPath(SkillInstallTarget target)
        {
            switch (target)
            {
                case SkillInstallTarget.GitHub:
                    return NormalizeDirectoryPath(Path.GetFullPath(".github/skills/"));
                case SkillInstallTarget.Claude:
                    return NormalizeDirectoryPath(Path.GetFullPath(".claude/skills/"));
                case SkillInstallTarget.Agents:
                    return NormalizeDirectoryPath(Path.GetFullPath(".agents/skills/"));
                case SkillInstallTarget.Custom:
                default:
                    return GetDefaultCustomSkillsTargetPath();
            }
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

            bool removed = AdditionalAssemblyNames.Remove(assemblyName);
            if (removed)
            {
                EditorUtility.SetDirty(this);
            }

            return removed;
        }

        public void SetInputActionsAssetPath(string path)
        {
            InputActionsAssetPath = string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : NormalizePath(path.Trim());
            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// Get the singleton instance
        /// </summary>
        public static UnityCodeMcpServerSettings Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }
                _instance = LoadSettingsAsset(_settingsAssetPath);
                if (_instance != null)
                {
                    _instance.InitializeSkillsTarget();
                    return _instance;
                }
                _instance = CreateInstance<UnityCodeMcpServerSettings>();
                _instance.InitializeSkillsTarget();

                SaveInstance(_instance);

                return _instance;
            }

        }

        public static void SaveInstance(UnityCodeMcpServerSettings instance)
        {
            if (instance == null)
            {
                Debug.LogWarning($"{Protocol.McpProtocol.LogPrefix} Cannot save null settings instance.");
                return;
            }
            instance.InitializeSkillsTarget();
            if (string.IsNullOrEmpty(_settingsAssetPath))
            {
                Debug.LogWarning($"{Protocol.McpProtocol.LogPrefix} Settings asset path is null or empty. Cannot save settings instance.");
                return;
            }
            if (System.IO.File.Exists(_settingsAssetPath))
            {
                return;
            }
            if (!AssetDatabase.IsValidFolder(System.IO.Path.GetDirectoryName(_settingsAssetPath)))
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_settingsAssetPath));
            }
            AssetDatabase.CreateAsset(instance, _settingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(_settingsAssetPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"{Protocol.McpProtocol.LogPrefix} Created new UnityCodeMcpServerSettings asset at {_settingsAssetPath}");
        }

        /// <summary>
        /// Start/stop servers according to the current startup selection.
        /// Exposed for tests and editor automation.
        /// </summary>
        public void ApplySelection()
        {
            ServerLifecycleCoordinator.UpdateServerState();
        }

        /// <summary>
        /// Show the settings asset in the inspector
        /// </summary>
        [MenuItem("Tools/UnityCodeMcpServer/Show or Create Settings")]
        public static void ShowSettings()
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(UnityCodeMcpServerSettings).Name}");
            UnityCodeMcpServerSettings settings;
            if (guids.Length > 0)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                settings = AssetDatabase.LoadAssetAtPath<UnityCodeMcpServerSettings>(assetPath);
            }
            else
            {
                settings = GetOrCreateSettingsAsset();
            }

            if (settings != null)
            {
                EditorGUIUtility.PingObject(settings);
                Selection.activeObject = settings;
            }
        }

        public static UnityCodeMcpServerSettings GetOrCreateSettingsAsset()
        {
            // Check if asset file already exists
            UnityCodeMcpServerSettings settingsAsset = LoadSettingsAsset(_settingsAssetPath);
            if (settingsAsset != null)
            {
                settingsAsset.InitializeSkillsTarget();
                return settingsAsset;
            }

            settingsAsset = CreateInstance<UnityCodeMcpServerSettings>();
            settingsAsset.InitializeSkillsTarget();
            SaveInstance(settingsAsset);
            return settingsAsset;
        }

        private static UnityCodeMcpServerSettings LoadSettingsAsset(string settingsAssetPath)
        {
            if (string.IsNullOrEmpty(settingsAssetPath))
            {
                Debug.LogWarning($"{Protocol.McpProtocol.LogPrefix} Settings asset path is null or empty.");
                return null;
            }

            UnityCodeMcpServerSettings settings = AssetDatabase.LoadAssetAtPath<UnityCodeMcpServerSettings>(settingsAssetPath);
            if (settings != null)
            {
                settings.InitializeSkillsTarget();
                return settings;
            }
            return null;
        }

        private static string GetStoredSkillsTargetPath(SkillInstallTarget target)
        {
            switch (target)
            {
                case SkillInstallTarget.GitHub:
                    return ".github/skills/";
                case SkillInstallTarget.Claude:
                    return ".claude/skills/";
                case SkillInstallTarget.Agents:
                case SkillInstallTarget.Custom:
                default:
                    return ".agents/skills/";
            }
        }

        private static string GetDefaultCustomSkillsTargetPath()
        {
            return NormalizePath(Path.GetFullPath("."));
        }

        private static bool IsAbsolutePath(string path)
        {
            return Path.IsPathRooted(path);
        }

        private static string NormalizeDirectoryPath(string path)
        {
            string normalized = NormalizePath(path);
            return normalized.EndsWith("/") ? normalized : normalized + "/";
        }

        private static string NormalizePath(string path)
        {
            return path.Replace("\\", "/");
        }
    }
}
