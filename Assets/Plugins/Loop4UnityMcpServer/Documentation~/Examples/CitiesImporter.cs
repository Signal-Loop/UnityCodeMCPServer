using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

static class CitiesImporter
{
    private const string CsvAssetPath = "Assets/Resources/cities.csv";
    private const string CityAssetsFolder = "Assets/ScriptableObjects";
    private const string DefaultSpritePath = "Assets/Images/City.png";

    public static object Run()
    {
        AssetDatabase.Refresh();

        // Wait briefly for compilation if scripts were just added.
        var start = DateTime.UtcNow;
        while (EditorApplication.isCompiling)
        {
            if ((DateTime.UtcNow - start).TotalSeconds > 60)
                throw new Exception("Unity is still compiling after 60s. Re-run the script once compilation finishes.");
            System.Threading.Thread.Sleep(200);
        }

        var citySoType = typeof(CitySO);
        var cityComponentType = typeof(CityComponent);

        EnsureFolder(CityAssetsFolder);

        var csvFullPath = Path.GetFullPath(CsvAssetPath);
        if (!File.Exists(csvFullPath))
            throw new FileNotFoundException($"CSV file not found at '{CsvAssetPath}' (full path '{csvFullPath}').");

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(DefaultSpritePath);
        if (sprite == null)
            throw new Exception($"Default sprite not found or not imported as Sprite at '{DefaultSpritePath}'.");

        var lines = File.ReadAllLines(csvFullPath);
        if (lines.Length < 2)
            throw new Exception("CSV has no data rows.");

        var header = ParseCsvLine(lines[0]);
        int idxName = IndexOfColumn(header, "CityName");
        int idxCoords = IndexOfColumn(header, "Coordinates");
        int idxDesc = IndexOfColumn(header, "Description");

        var createdAssets = 0;
        var updatedAssets = 0;

        var cityAssets = new List<CitySO>();

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            var row = ParseCsvLine(lines[i]);
            if (row.Count <= Math.Max(idxDesc, Math.Max(idxName, idxCoords)))
                continue;

            var cityName = row[idxName]?.Trim();
            if (string.IsNullOrWhiteSpace(cityName))
                continue;

            var coordsText = row[idxCoords] ?? string.Empty;
            var description = row[idxDesc] ?? string.Empty;

            var coords = ParseCoordinates(coordsText);

            var assetPath = $"{CityAssetsFolder}/{SanitizeFileName(cityName)}.asset";
            var citySo = AssetDatabase.LoadAssetAtPath<CitySO>(assetPath);
            if (citySo == null)
            {
                citySo = ScriptableObject.CreateInstance<CitySO>();
                AssetDatabase.CreateAsset(citySo, assetPath);
                createdAssets++;
            }
            else
            {
                updatedAssets++;
            }

            citySo.CityName = cityName;
            citySo.Coordinates = coords;
            citySo.Description = description;
            EditorUtility.SetDirty(citySo);

            cityAssets.Add(citySo);
        }

        AssetDatabase.SaveAssets();

        // Scene objects
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
            throw new Exception("No valid active scene.");

        var map = FindOrCreateMapRoot(scene);

        var createdGos = 0;
        var updatedGos = 0;

        foreach (var citySo in cityAssets)
        {
            var cityGo = FindOrCreateChild(map.transform, citySo.CityName);
            cityGo.transform.localPosition = new Vector3(citySo.Coordinates.x, citySo.Coordinates.y, 0f);

            var component = cityGo.GetComponent<CityComponent>();
            if (component == null)
                component = cityGo.AddComponent<CityComponent>();
            component.City = citySo;

            var sr = cityGo.GetComponent<SpriteRenderer>();
            if (sr == null)
                sr = cityGo.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;

            if (PrefabUtility.IsPartOfAnyPrefab(cityGo))
            {
                // Avoid making prefab overrides silently if this is in a prefab instance.
            }

            if (cityGo.hideFlags == HideFlags.None)
            {
                // Count create/update heuristically.
            }

            // Determine counts by whether it had a parent already.
            if (cityGo.transform.parent == map.transform && cityGo.transform.GetSiblingIndex() >= 0)
                updatedGos++;
            else
                createdGos++;
        }

        EditorSceneManager.MarkSceneDirty(scene);

        return $"Cities import complete. Assets: {createdAssets} created, {updatedAssets} updated. Cities in scene: {cityAssets.Count}. Map: '{map.name}'.";
    }

    private static int IndexOfColumn(List<string> header, string name)
    {
        for (int i = 0; i < header.Count; i++)
        {
            if (string.Equals(header[i]?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        throw new Exception($"CSV missing required column '{name}'. Found: {string.Join(", ", header)}");
    }

    private static Vector2 ParseCoordinates(string text)
    {
        // Expected format: "x; y" (semicolon-separated)
        var parts = text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            throw new Exception($"Invalid Coordinates '{text}'. Expected 'x; y'.");

        var x = float.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
        var y = float.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
        return new Vector2(x, y);
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        if (line == null)
            return result;

        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    // Escaped quote
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                continue;
            }

            if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        result.Add(sb.ToString());
        return result;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        return sb.ToString().Trim();
    }

    private static void EnsureFolder(string assetsPath)
    {
        if (AssetDatabase.IsValidFolder(assetsPath))
            return;

        var parts = assetsPath.Split('/');
        if (parts.Length == 0 || parts[0] != "Assets")
            throw new Exception($"Folder must be under Assets/. Got '{assetsPath}'.");

        var current = "Assets";
        for (int i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static GameObject FindOrCreateMapRoot(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root != null && root.name == "Map")
                return root;
        }

        var map = new GameObject("Map");
        SceneManager.MoveGameObjectToScene(map, scene);
        return map;
    }

    private static GameObject FindOrCreateChild(Transform parent, string childName)
    {
        var existing = parent.Find(childName);
        if (existing != null)
            return existing.gameObject;

        var go = new GameObject(childName);
        go.transform.SetParent(parent, false);
        return go;
    }
}

return CitiesImporter.Run();
