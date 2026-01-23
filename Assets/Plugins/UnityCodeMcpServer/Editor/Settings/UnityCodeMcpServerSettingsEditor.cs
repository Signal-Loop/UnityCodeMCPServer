using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityCodeMcpServer.Settings.Editor
{
    /// <summary>
    /// Custom editor for UnityCodeMcpServerSettings with assembly selector UI
    /// </summary>
    [CustomEditor(typeof(UnityCodeMcpServerSettings))]
    public class UnityCodeMcpServerSettingsEditor : UnityEditor.Editor
    {
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

            DrawPropertiesExcluding(serializedObject, "m_Script", "AdditionalAssemblyNames");

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
                            Debug.Log($"{Protocol.McpProtocol.LogPrefix} Added assembly: {assemblyName}");
                            RefreshAvailableAssemblies();
                        }
                        else
                        {
                            Debug.LogWarning($"{Protocol.McpProtocol.LogPrefix} Assembly already added or is a default assembly: {assemblyName}");
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
                            Debug.Log($"{Protocol.McpProtocol.LogPrefix} Removed assembly: {assemblyName}");
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
        }

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
                Debug.LogError($"{Protocol.McpProtocol.LogPrefix} Error refreshing assemblies: {ex.Message}");
                _availableAssemblyNames = new[] { "(Error loading assemblies)" };
                _selectedAssemblyIndex = 0;
            }
        }
    }
}
