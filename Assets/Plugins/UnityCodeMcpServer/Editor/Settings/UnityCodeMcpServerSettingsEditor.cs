using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityCodeMcpServer.Editor.Installer;
using UnityCodeMcpServer.Helpers;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UnityCodeMcpServer.Settings.Editor
{
    /// <summary>
    /// Custom editor for UnityCodeMcpServerSettings with assembly selector UI and skills installer.
    /// </summary>
    [CustomEditor(typeof(UnityCodeMcpServerSettings))]
    public class UnityCodeMcpServerSettingsEditor : UnityEditor.Editor
    {
        private const string NoAssembliesAvailableLabel = "(No assemblies available)";

        // ── Assembly selector state ───────────────────────────────────────────
        private readonly AdvancedDropdownState _assemblyDropdownState = new();
        private string[] _availableAssemblyNames;
        private bool _showDefaultAssemblies = true;
        private bool _showAdditionalAssemblies = true;

        private void OnEnable()
        {
            RefreshAvailableAssemblies();
        }

        public override void OnInspectorGUI()
        {
            UnityCodeMcpServerSettings settings = (UnityCodeMcpServerSettings)target;
            bool wasDirtyBeforeGui = EditorUtility.IsDirty(settings);
            serializedObject.Update();

            DrawPropertiesExcluding(serializedObject, "m_Script", "AdditionalAssemblyNames", "InputActionsAssetPath", "SkillsInstallTarget", "SkillsTargetPath");

            EditorGUILayout.Space();
            DrawInputActionsSection(settings);
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
                foreach (string assemblyName in UnityCodeMcpServerSettings.DefaultAssemblyNames)
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

                using (new EditorGUI.DisabledScope(!HasAvailableAssemblies()))
                {
                    if (EditorGUILayout.DropdownButton(
                        new GUIContent(GetAssemblyDropdownLabel()),
                        FocusType.Keyboard))
                    {
                        Rect buttonRect = GUILayoutUtility.GetLastRect();
                        AssemblySearchableDropdown dropdown = new(
                            _assemblyDropdownState,
                            _availableAssemblyNames,
                            assemblyName => HandleAssemblySelected(assemblyName));
                        dropdown.Show(buttonRect);
                    }
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(5);

                // List of additional assemblies with remove buttons
                if (settings.AdditionalAssemblyNames != null && settings.AdditionalAssemblyNames.Count > 0)
                {
                    List<string> assembliesToRemove = new();

                    foreach (string assemblyName in settings.AdditionalAssemblyNames)
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
                    foreach (string assemblyName in assembliesToRemove)
                    {
                        if (settings.RemoveAssembly(assemblyName))
                        {
                            UnityCodeMcpServerLogger.Info($"{Protocol.McpProtocol.LogPrefix} Removed assembly: {assemblyName}");
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

            bool appliedChanges = serializedObject.ApplyModifiedProperties();
            if ((wasDirtyBeforeGui || appliedChanges || EditorUtility.IsDirty(settings))
                && !EditorApplication.isUpdating
                && !EditorApplication.isCompiling)
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
            }
        }

        private void DrawInputActionsSection(UnityCodeMcpServerSettings settings)
        {
            EditorGUILayout.LabelField("Input Actions", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "play_unity_game resolves this InputActionAsset path on every call. " +
                "If left empty, the tool falls back to the first InputActionAsset under Assets, then the first one found anywhere.",
                MessageType.Info);

            InputActionAsset currentAsset = string.IsNullOrWhiteSpace(settings.InputActionsAssetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<InputActionAsset>(settings.InputActionsAssetPath);

            InputActionAsset selectedAsset = (InputActionAsset)EditorGUILayout.ObjectField(
                "Input Actions Asset",
                currentAsset,
                typeof(InputActionAsset),
                false);

            if (selectedAsset != currentAsset)
            {
                string assetPath = selectedAsset == null ? string.Empty : AssetDatabase.GetAssetPath(selectedAsset);
                settings.SetInputActionsAssetPath(assetPath);
            }

            string enteredPath = EditorGUILayout.DelayedTextField("Asset Path", settings.InputActionsAssetPath);
            if (!string.Equals(enteredPath, settings.InputActionsAssetPath, StringComparison.Ordinal))
            {
                settings.SetInputActionsAssetPath(enteredPath);
            }

            if (!string.IsNullOrWhiteSpace(settings.InputActionsAssetPath) && currentAsset == null)
            {
                EditorGUILayout.HelpBox(
                    $"No InputActionAsset was found at '{settings.InputActionsAssetPath}'. The play tool will fall back to discovery.",
                    MessageType.Warning);
            }
        }

        // ── Skills installer section ──────────────────────────────────────────

        private void DrawSkillsInstallerSection()
        {
            UnityCodeMcpServerSettings settings = (UnityCodeMcpServerSettings)target;
            settings.InitializeSkillsTarget();

            EditorGUILayout.LabelField("Skills", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Bundled skill files are installed automatically when the package is installed or updated. " +
                "Only new or changed skill files are copied.",
                MessageType.Info);

            EditorGUILayout.Space(4);

            UnityCodeMcpServerSettings.SkillInstallTarget selectedTarget = (UnityCodeMcpServerSettings.SkillInstallTarget)EditorGUILayout.EnumPopup(
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
                UnityCodeMcpServerLogger.Warn($"{Protocol.McpProtocol.LogPrefix} Could not locate the Skills source directory within the package. Skipping skill relocation.");
                return;
            }

            IFileSystem fileSystem = new EditorFileSystem();
            SkillsInstaller installer = new(fileSystem);
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

            UnityEditor.PackageManager.PackageInfo packageInfo = UnityEditor.PackageManager.PackageInfo
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

        private bool HandleAssemblySelected(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName) || IsPlaceholderAssemblyName(assemblyName))
            {
                return false;
            }

            UnityCodeMcpServerSettings settings = (UnityCodeMcpServerSettings)target;
            if (settings.AddAssembly(assemblyName))
            {
                UnityCodeMcpServerLogger.Info($"{Protocol.McpProtocol.LogPrefix} Added assembly: {assemblyName}");
                RefreshAvailableAssemblies();
                Repaint();
                return true;
            }

            UnityCodeMcpServerLogger.Warn($"{Protocol.McpProtocol.LogPrefix} Assembly already added or is a default assembly: {assemblyName}");
            return false;
        }

        private bool HasAvailableAssemblies()
        {
            return _availableAssemblyNames != null &&
                _availableAssemblyNames.Length > 0 &&
                !IsPlaceholderAssemblyName(_availableAssemblyNames[0]);
        }

        private string GetAssemblyDropdownLabel()
        {
            return HasAvailableAssemblies()
                ? "Select Assembly..."
                : NoAssembliesAvailableLabel;
        }

        private static bool IsPlaceholderAssemblyName(string assemblyName)
        {
            return string.Equals(assemblyName, NoAssembliesAvailableLabel, StringComparison.Ordinal);
        }

        private void RefreshAvailableAssemblies()
        {
            try
            {
                Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                UnityCodeMcpServerSettings settings = (UnityCodeMcpServerSettings)target;

                // Get assembly names that are not already in default or additional lists
                HashSet<string> existingNames = new(UnityCodeMcpServerSettings.DefaultAssemblyNames);
                if (settings.AdditionalAssemblyNames != null)
                {
                    foreach (string name in settings.AdditionalAssemblyNames)
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
                    _availableAssemblyNames = new[] { NoAssembliesAvailableLabel };
                }
            }
            catch (Exception ex)
            {
                UnityCodeMcpServerLogger.Error($"{Protocol.McpProtocol.LogPrefix} Error refreshing assemblies: {ex.Message}");
                _availableAssemblyNames = new[] { "(Error loading assemblies)" };
            }
        }

        private sealed class AssemblySearchableDropdown : AdvancedDropdown
        {
            private readonly string[] _assemblyNames;
            private readonly Action<string> _onAssemblySelected;

            public AssemblySearchableDropdown(
                AdvancedDropdownState state,
                string[] assemblyNames,
                Action<string> onAssemblySelected)
                : base(state)
            {
                _assemblyNames = assemblyNames ?? Array.Empty<string>();
                _onAssemblySelected = onAssemblySelected;
                minimumSize = new Vector2(260f, 300f);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                AdvancedDropdownItem root = new("Assemblies");

                for (int index = 0; index < _assemblyNames.Length; index++)
                {
                    root.AddChild(new AdvancedDropdownItem(_assemblyNames[index])
                    {
                        id = index,
                    });
                }

                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                base.ItemSelected(item);
                _onAssemblySelected?.Invoke(item.name);
            }
        }
    }
}
