using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityCodeMcpServer.Editor.Installer;
using UnityCodeMcpServer.Helpers;
using UnityEditor;
using UnityEngine;

namespace UnityCodeMcpServer.Settings.Editor
{
    /// <summary>
    /// Custom editor for UnityCodeMcpServerSettings with assembly selector UI and skills installer.
    /// </summary>
    [CustomEditor(typeof(UnityCodeMcpServerSettings))]
    public class UnityCodeMcpServerSettingsEditor : UnityEditor.Editor
    {
        // ── Assembly selector state ───────────────────────────────────────────
        private int _selectedAssemblyIndex = 0;
        private string[] _availableAssemblyNames;
        private bool _showDefaultAssemblies = true;
        private bool _showAdditionalAssemblies = true;

        // ── Skills installer state ────────────────────────────────────────────
        private string _lastSkillsInstallMessage = string.Empty;
        private MessageType _lastSkillsInstallMessageType = MessageType.Info;

        private static readonly (string Label, string RelativePath)[] SkillsPresets =
        {
            (".github/skills/", ".github/skills/"),
            (".claude/skills/", ".claude/skills/"),
            (".agents/skills/", ".agents/skills/"),
        };

        private string GetSelectedSkillsTargetPath(UnityCodeMcpServerSettings settings)
        {
            if (string.IsNullOrEmpty(settings.SkillsTargetPath))
                return Path.GetFullPath(".");
            return settings.SkillsTargetPath;
        }

        private void SetSelectedSkillsTargetPath(UnityCodeMcpServerSettings settings, string path)
        {
            if (settings.SkillsTargetPath != path)
            {
                settings.SkillsTargetPath = path;
                EditorUtility.SetDirty(settings);
            }
        }

        private void OnEnable()
        {
            RefreshAvailableAssemblies();
        }

        public override void OnInspectorGUI()
        {
            var settings = (UnityCodeMcpServerSettings)target;
            serializedObject.Update();

            // Draw default properties
            EditorGUILayout.LabelField("Server Configuration", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            DrawPropertiesExcluding(serializedObject, "m_Script", "AdditionalAssemblyNames");

            EditorGUILayout.Space();
            DrawSkillsInstallerSection();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Script Execution Assemblies", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "These assemblies are loaded for C# script execution. " +
                "Additional assemblies from the current AppDomain can be added using the selector below.",
                MessageType.Info);

            EditorGUILayout.Space();

            // Default assemblies section (readonly)
            _showDefaultAssemblies = EditorGUILayout.Foldout(_showDefaultAssemblies, "Default Assemblies (Read-Only)", true);
            if (_showDefaultAssemblies)
            {
                EditorGUI.indentLevel++;
                GUI.enabled = false;
                foreach (var assemblyName in UnityCodeMcpServerSettings.DefaultAssemblyNames)
                {
                    EditorGUILayout.LabelField("• " + assemblyName, EditorStyles.label);
                }
                GUI.enabled = true;
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            // Additional assemblies section (editable)
            _showAdditionalAssemblies = EditorGUILayout.Foldout(_showAdditionalAssemblies, "Additional Assemblies", true);
            if (_showAdditionalAssemblies)
            {
                EditorGUI.indentLevel++;

                // Assembly selector dropdown
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Add Assembly:", GUILayout.Width(100));

                _selectedAssemblyIndex = EditorGUILayout.Popup(_selectedAssemblyIndex, _availableAssemblyNames);

                if (GUILayout.Button("Add", GUILayout.Width(50)))
                {
                    if (_availableAssemblyNames != null && _availableAssemblyNames.Length > 0 &&
                        _selectedAssemblyIndex >= 0 && _selectedAssemblyIndex < _availableAssemblyNames.Length)
                    {
                        var assemblyName = _availableAssemblyNames[_selectedAssemblyIndex];
                        if (settings.AddAssembly(assemblyName))
                        {
                            LoopLogger.Info($"{Protocol.McpProtocol.LogPrefix} Added assembly: {assemblyName}");
                            RefreshAvailableAssemblies();
                        }
                        else
                        {
                            LoopLogger.Warn($"{Protocol.McpProtocol.LogPrefix} Assembly already added or is a default assembly: {assemblyName}");
                        }
                    }
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(5);

                // List of additional assemblies with remove buttons
                if (settings.AdditionalAssemblyNames != null && settings.AdditionalAssemblyNames.Count > 0)
                {
                    var assembliesToRemove = new List<string>();

                    foreach (var assemblyName in settings.AdditionalAssemblyNames)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("• " + assemblyName);
                        if (GUILayout.Button("Remove", GUILayout.Width(70)))
                        {
                            assembliesToRemove.Add(assemblyName);
                        }
                        EditorGUILayout.EndHorizontal();
                    }

                    // Remove marked assemblies
                    foreach (var assemblyName in assembliesToRemove)
                    {
                        if (settings.RemoveAssembly(assemblyName))
                        {
                            LoopLogger.Info($"{Protocol.McpProtocol.LogPrefix} Removed assembly: {assemblyName}");
                            RefreshAvailableAssemblies();
                        }
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("(none)", EditorStyles.miniLabel);
                }

                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        // ── Skills installer section ──────────────────────────────────────────

        private void DrawSkillsInstallerSection()
        {
            var settings = (UnityCodeMcpServerSettings)target;
            string selectedPath = GetSelectedSkillsTargetPath(settings);

            EditorGUILayout.LabelField("Skills", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Install built-in skill files into an AI agent's skills directory so that " +
                "the agent can discover and use them automatically.",
                MessageType.Info);

            EditorGUILayout.Space(4);

            // Quick-select presets
            EditorGUILayout.LabelField("Quick Select:", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            foreach (var (label, relativePath) in SkillsPresets)
            {
                if (GUILayout.Button(label, EditorStyles.miniButton))
                    SetSelectedSkillsTargetPath(settings, Path.GetFullPath(relativePath));
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // Manual path selector
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Target Directory:", GUILayout.Width(110));
            string newPath = EditorGUILayout.TextField(selectedPath);
            if (newPath != selectedPath)
                SetSelectedSkillsTargetPath(settings, newPath);
            selectedPath = newPath;

            if (GUILayout.Button("Custom", GUILayout.Width(60)))
            {
                string chosen = EditorUtility.OpenFolderPanel(
                    "Select skills target folder",
                    selectedPath,
                    string.Empty);
                if (!string.IsNullOrEmpty(chosen))
                    SetSelectedSkillsTargetPath(settings, chosen);
                selectedPath = GetSelectedSkillsTargetPath(settings);
            }
            EditorGUILayout.EndHorizontal();

            // Resolved absolute path hint
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(selectedPath, EditorStyles.miniLabel);
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(4);

            if (GUILayout.Button("Install / Update Skills"))
            {
                RunSkillsInstall(selectedPath);
            }

            if (!string.IsNullOrEmpty(_lastSkillsInstallMessage))
            {
                EditorGUILayout.HelpBox(_lastSkillsInstallMessage, _lastSkillsInstallMessageType);
            }
        }

        private void RunSkillsInstall(string targetPath)
        {
            string sourcePath = ResolveSkillsSourcePath();

            if (string.IsNullOrEmpty(sourcePath))
            {
                _lastSkillsInstallMessage = "Could not locate the Skills source directory within the package.";
                _lastSkillsInstallMessageType = MessageType.Error;
                LoopLogger.Error($"{Protocol.McpProtocol.LogPrefix} {_lastSkillsInstallMessage}");
                return;
            }

            IFileSystem fileSystem = new EditorFileSystem();
            var installer = new SkillsInstaller(fileSystem);
            SkillsInstallResult result = installer.Install(sourcePath, targetPath);

            _lastSkillsInstallMessage = result.ToString();
            _lastSkillsInstallMessageType = result.Success ? MessageType.Info : MessageType.Error;
        }

        /// <summary>
        /// Resolve the Skills source directory from the package root.
        /// Works in both package-cache and embedded/local package contexts.
        /// </summary>
        public static string ResolveSkillsSourcePath()
        {
            const string relativePath = "Editor/Skills";

            var packageInfo = UnityEditor.PackageManager.PackageInfo
                .FindForAssembly(typeof(UnityCodeMcpServerSettingsEditor).Assembly);

            if (packageInfo != null)
            {
                string candidate = Path.Combine(packageInfo.resolvedPath, relativePath);
                if (Directory.Exists(candidate))
                    return candidate;
            }

            // Fallback: assets are stored directly under Assets/Plugins/UnityCodeMcpServer
            string fallback = Path.GetFullPath(
                Path.Combine("Assets", "Plugins", "UnityCodeMcpServer", relativePath));

            return Directory.Exists(fallback) ? fallback : null;
        }

        // ── Assembly selector section ─────────────────────────────────────────

        private void RefreshAvailableAssemblies()
        {
            try
            {
                var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                var settings = (UnityCodeMcpServerSettings)target;

                // Get assembly names that are not already in default or additional lists
                var existingNames = new HashSet<string>(UnityCodeMcpServerSettings.DefaultAssemblyNames);
                if (settings.AdditionalAssemblyNames != null)
                {
                    foreach (var name in settings.AdditionalAssemblyNames)
                    {
                        existingNames.Add(name);
                    }
                }

                _availableAssemblyNames = loadedAssemblies
                    .Select(a => a.GetName().Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name) && !existingNames.Contains(name))
                    .OrderBy(name => name)
                    .ToArray();

                if (_availableAssemblyNames.Length == 0)
                {
                    _availableAssemblyNames = new[] { "(No assemblies available)" };
                }

                _selectedAssemblyIndex = 0;
            }
            catch (Exception ex)
            {
                LoopLogger.Error($"{Protocol.McpProtocol.LogPrefix} Error refreshing assemblies: {ex.Message}");
                _availableAssemblyNames = new[] { "(Error loading assemblies)" };
                _selectedAssemblyIndex = 0;
            }
        }
    }
}
