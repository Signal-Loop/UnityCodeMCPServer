using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using UnityEditor;
using UnityEngine;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Services;

namespace UnityCodeMcpServer.Editor.EditorTools
{
    public class FavouritesWindow : EditorWindow
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly List<FavouriteScript> _favourites = new List<FavouriteScript>();
        [SerializeField] private string _selectedScriptName = string.Empty;
        private string _scriptName = string.Empty;
        private string _scriptContent = string.Empty;
        private Vector2 _scriptScrollPosition;
        private int _selectedFavouriteIndex = -1;
        private GUIStyle _textAreaStyle;
        private ScriptExecutionService _scriptExecutionService;

        private static string _favouritesDirectory;
        private static string _favouritesPath;
        private static string FavouritesDirectory => _favouritesDirectory ??= Path.Combine(Application.dataPath, "..", ".unityCodeMcpServer");
        private static string FavouritesPath => _favouritesPath ??= Path.Combine(FavouritesDirectory, "favouriteScripts.json");

        [MenuItem("Tools/UnityCodeMcpServer/Favourite Scripts")]
        public static void ShowWindow()
        {
            GetWindow<FavouritesWindow>("UnityCodeMcpServer Favourite Scripts");
        }

        private void OnEnable()
        {
            _scriptExecutionService = new ScriptExecutionService();
            ReloadFavourites();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);

            DrawFavouriteDropdown();

            EditorGUILayout.Space(10);

            DrawScriptNameField();

            EditorGUILayout.Space(10);

            DrawScriptContentArea();

            EditorGUILayout.Space(10);

            DrawActionButtons();

            DrawRunButton();
        }

        private void DrawFavouriteDropdown()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Scripts", EditorStyles.boldLabel);            
            EditorGUILayout.EndHorizontal();

            // Create dropdown options including "New Script" at index 0
            string[] names = new string[_favourites.Count + 1];
            names[0] = "<New Script>";
            for (int i = 0; i < _favourites.Count; i++)
            {
                names[i + 1] = _favourites[i].Name;
            }

            // Map selected index: -1 or new script maps to 0, otherwise add 1 offset
            int displayIndex = _selectedFavouriteIndex < 0 ? 0 : _selectedFavouriteIndex + 1;
            displayIndex = Mathf.Clamp(displayIndex, 0, names.Length - 1);
            
            int newDisplayIndex = EditorGUILayout.Popup("Select Script", displayIndex, names);

            // Map back from display index to actual index
            if (newDisplayIndex == 0)
            {
                if (_selectedFavouriteIndex != -1)
                {
                    CreateNewScript();
                }
            }
            else
            {
                int newIndex = newDisplayIndex - 1;
                if (newIndex != _selectedFavouriteIndex)
                {
                    _selectedFavouriteIndex = newIndex;
                    LoadSelectedScript();
                }
            }
        }

        private void DrawScriptNameField()
        {
            EditorGUILayout.LabelField("Script Name", EditorStyles.boldLabel);
            _scriptName = EditorGUILayout.TextField("Name", _scriptName);
        }

        private void DrawScriptContentArea()
        {
            EditorGUILayout.LabelField("Script", EditorStyles.boldLabel);

            _textAreaStyle ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };

            _scriptScrollPosition = EditorGUILayout.BeginScrollView(_scriptScrollPosition, GUILayout.Height(300));
            _scriptContent = EditorGUILayout.TextArea(_scriptContent, _textAreaStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("+", GUILayout.Height(20)))
            {
                CreateNewScript();
            }

            if (GUILayout.Button("Save", GUILayout.Height(20)))
            {
                SaveCurrent();
            }

            if (GUILayout.Button("Reload", GUILayout.Height(20)))
            {
                ReloadFavourites();
            }

            if (GUILayout.Button("Delete", GUILayout.Height(20)))
            {
                DeleteCurrent();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawRunButton()
        {
            if (GUILayout.Button("Run", GUILayout.Height(30)))
            {
                RunScript();
            }
        }

        private void CreateNewScript()
        {
            _selectedFavouriteIndex = -1;
            _scriptName = string.Empty;
            _scriptContent = string.Empty;
            _selectedScriptName = string.Empty;
        }

        private void LoadSelectedScript()
        {
            if (_selectedFavouriteIndex >= 0 && _selectedFavouriteIndex < _favourites.Count)
            {
                var selected = _favourites[_selectedFavouriteIndex];
                _scriptName = selected.Name;
                _scriptContent = selected.Script;
                _selectedScriptName = selected.Name;
            }
        }

        private void LoadScript()
        {
            if (_favourites.Count == 0)
            {
                _selectedFavouriteIndex = -1;
                _selectedScriptName = string.Empty;
                _scriptName = string.Empty;
                _scriptContent = "Debug.Log(\"Hello!\");\nreturn 2 + 3;";
                return;
            }

            // Try to load the previously selected script
            if (!string.IsNullOrWhiteSpace(_selectedScriptName))
            {
                int index = _favourites.FindIndex(f => string.Equals(f.Name, _selectedScriptName, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    _selectedFavouriteIndex = index;
                    LoadSelectedScript();
                    return;
                }
            }

            // Fall back to loading the first script
            _selectedFavouriteIndex = 0;
            LoadSelectedScript();
        }

        private void ReloadFavourites()
        {
            _favourites.Clear();
            EnsureDirectoryExists();

            if (File.Exists(FavouritesPath))
            {
                try
                {
                    string json = File.ReadAllText(FavouritesPath);
                    var loaded = JsonSerializer.Deserialize<List<FavouriteScript>>(json);
                    if (loaded != null)
                    {
                        _favourites.AddRange(loaded);
                    }
                }
                catch (Exception ex)
                {
                    LoopLogger.Error($"Failed to load favourites: {ex.Message}");
                }
            }

            LoadScript();
            Repaint();
        }

        private void SaveCurrent()
        {
            if (string.IsNullOrWhiteSpace(_scriptName))
            {
                LoopLogger.Warn("Script name is required to save a favourite.");
                return;
            }

            var entry = new FavouriteScript
            {
                Name = _scriptName.Trim(),
                Script = _scriptContent ?? string.Empty
            };

            int existingIndex = _favourites.FindIndex(f => string.Equals(f.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                _favourites[existingIndex] = entry;
                _selectedFavouriteIndex = existingIndex;
            }
            else
            {
                _favourites.Add(entry);
                _selectedFavouriteIndex = _favourites.Count - 1;
            }

            _selectedScriptName = entry.Name;
            PersistFavourites();
            LoopLogger.Debug($"Saved favourite '{entry.Name}'.");
            Repaint();
        }

        private void DeleteCurrent()
        {
            if (string.IsNullOrWhiteSpace(_scriptName))
            {
                LoopLogger.Warn("No script name provided to delete.");
                return;
            }

            int removed = _favourites.RemoveAll(f => string.Equals(f.Name, _scriptName, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                LoopLogger.Warn($"Favourite '{_scriptName}' not found.");
                return;
            }

            PersistFavourites();
            ReloadFavourites();
            LoopLogger.Debug($"Deleted favourite '{_scriptName}'.");
        }

        private void PersistFavourites()
        {
            EnsureDirectoryExists();
            string json = JsonSerializer.Serialize(_favourites, JsonOptions);
            File.WriteAllText(FavouritesPath, json);
        }

        private void EnsureDirectoryExists()
        {
            if (!Directory.Exists(FavouritesDirectory))
            {
                Directory.CreateDirectory(FavouritesDirectory);
            }
        }

        private async void RunScript()
        {
            if (string.IsNullOrWhiteSpace(_scriptContent))
            {
                Debug.LogWarning("Cannot run an empty script.");
                return;
            }

            try
            {
                var result = await _scriptExecutionService.ExecuteScriptAsync(_scriptContent);

                var output = FormatExecutionOutput(result);
                LoopLogger.Info(output);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error executing script with ExecuteCSharpScriptInUnityEditor: {ex}");
            }

            Repaint();
        }

        private string FormatExecutionOutput(ScriptExecutionService.ExecutionResult result)
        {
            var output = new System.Text.StringBuilder();
            output.AppendLine($"Script Execution Status: {result.Status}, Result: {result.ResultText}");

            if (!string.IsNullOrWhiteSpace(result.Logs))
            {
                output.AppendLine($"\n-----\nLogs:\n{result.Logs}");
            }

            if (!string.IsNullOrWhiteSpace(result.Errors))
            {
                output.AppendLine($"\n-----\nErrors:\n{result.Errors}");
            }

            return output.ToString();
        }

        [Serializable]
        public class FavouriteScript
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("script")]
            public string Script { get; set; }
        }
    }
}
