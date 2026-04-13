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

            DrawPropertiesExcluding(serializedObject, "m_Script", "AdditionalAssemblyNames", "SkillsInstallTarget", "SkillsTargetPath");

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
            if (EditorUtility.IsDirty(settings) && !EditorApplication.isUpdating && !EditorApplication.isCompiling)
            {
                AssetDatabase.SaveAssetIfDirty(settings);
            }
        }

        // ── Skills installer section ──────────────────────────────────────────

        private void DrawSkillsInstallerSection()
        {
            var settings = (UnityCodeMcpServerSettings)target;
            settings.InitializeSkillsTarget();

            EditorGUILayout.LabelField("Skills", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Bundled skill files are installed automatically when the package is installed or updated. " +
                "Only new or changed skill files are copied.",
                MessageType.Info);

            EditorGUILayout.Space(4);

            var selectedTarget = (UnityCodeMcpServerSettings.SkillInstallTarget)EditorGUILayout.EnumPopup(
                "Install Directory",
                settings.SkillsInstallTarget);
            if (selectedTarget != settings.SkillsInstallTarget)
            {
                UpdateSkillsTarget(settings, () => settings.SetSkillsInstallTarget(selectedTarget));
            }

            EditorGUILayout.Space(4);

            if (settings.SkillsInstallTarget == UnityCodeMcpServerSettings.SkillInstallTarget.Custom)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Custom Folder", GUILayout.Width(110));
                string customPath = EditorGUILayout.DelayedTextField(settings.SkillsTargetPath);
                if (customPath != settings.SkillsTargetPath)
                {
                    UpdateSkillsTarget(settings, () => settings.SetCustomSkillsTargetPath(customPath));
                }

                if (GUILayout.Button("Browse", GUILayout.Width(70)))
                {
                    string chosen = EditorUtility.OpenFolderPanel(
                        "Select skills target folder",
                        string.IsNullOrWhiteSpace(settings.SkillsTargetPath)
                            ? Path.GetFullPath(".")
                            : settings.SkillsTargetPath,
                        string.Empty);
                    if (!string.IsNullOrEmpty(chosen))
                    {
                        UpdateSkillsTarget(settings, () => settings.SetCustomSkillsTargetPath(chosen));
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(4);
            }

            EditorGUILayout.LabelField("Current Target Directory", settings.GetEffectiveSkillsTargetPath(), EditorStyles.wordWrappedMiniLabel);
        }

        private void UpdateSkillsTarget(UnityCodeMcpServerSettings settings, Action updateTarget)
        {
            string previousTargetPath = settings.GetEffectiveSkillsTargetPath();
            updateTarget();
            string newTargetPath = settings.GetEffectiveSkillsTargetPath();

            if (string.Equals(previousTargetPath, newTargetPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string sourcePath = ResolveSkillsSourcePath();
            if (string.IsNullOrEmpty(sourcePath))
            {
                LoopLogger.Warn($"{Protocol.McpProtocol.LogPrefix} Could not locate the Skills source directory within the package. Skipping skill relocation.");
                return;
            }

            IFileSystem fileSystem = new EditorFileSystem();
            var installer = new SkillsInstaller(fileSystem);
            bool changed = installer.RelocateInstalledSkills(sourcePath, previousTargetPath, newTargetPath);
            if (changed)
            {
                AssetDatabase.Refresh();
            }
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
