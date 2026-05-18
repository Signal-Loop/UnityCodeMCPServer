using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityCodeMcpServer.Settings;
using UnityCodeMcpServer.Settings.Editor;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityCodeMcpServer.Tests.EditMode
{
    [TestFixture]
    public class UnityCodeMcpServerSettingsTests
    {
        [Test]
        public void DefaultLogToFile_IsFalse()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                Assert.That(settings.LogToFile, Is.False);
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void ShowSettings_FindsExistingAsset()
        {
            // Call ShowSettings - it should find an existing asset or create one
            UnityCodeMcpServerSettings.ShowSettings();

            // Verify an asset was selected
            Assert.That(Selection.activeObject, Is.Not.Null);
            Assert.That(Selection.activeObject, Is.InstanceOf<UnityCodeMcpServerSettings>());
        }

        [Test]
        public void DefaultAssemblyNames_ContainsExpectedAssemblies()
        {
            Assert.That(UnityCodeMcpServerSettings.DefaultAssemblyNames, Contains.Item("System.Core"));
            Assert.That(UnityCodeMcpServerSettings.DefaultAssemblyNames, Contains.Item("UnityEngine.CoreModule"));
            Assert.That(UnityCodeMcpServerSettings.DefaultAssemblyNames, Contains.Item("Assembly-CSharp"));
            Assert.That(UnityCodeMcpServerSettings.DefaultAssemblyNames, Contains.Item("Assembly-CSharp-Editor"));
        }

        [Test]
        public void GetAllAssemblyNames_ReturnsDefaultsWhenNoAdditional()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                string[] allAssemblies = settings.GetAllAssemblyNames();

                Assert.That(allAssemblies, Is.EquivalentTo(UnityCodeMcpServerSettings.DefaultAssemblyNames));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void GetAllAssemblyNames_CombinesDefaultAndAdditional()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                settings.AdditionalAssemblyNames.Add("CustomAssembly1");
                settings.AdditionalAssemblyNames.Add("CustomAssembly2");

                string[] allAssemblies = settings.GetAllAssemblyNames();

                Assert.That(allAssemblies, Contains.Item("CustomAssembly1"));
                Assert.That(allAssemblies, Contains.Item("CustomAssembly2"));
                Assert.That(allAssemblies.Length, Is.EqualTo(UnityCodeMcpServerSettings.DefaultAssemblyNames.Length + 2));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void GetAllAssemblyNames_RemovesDuplicates()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                settings.AdditionalAssemblyNames.Add("CustomAssembly");
                settings.AdditionalAssemblyNames.Add("CustomAssembly");

                string[] allAssemblies = settings.GetAllAssemblyNames();

                Assert.That(allAssemblies.Count(assembly => assembly == "CustomAssembly"), Is.EqualTo(1));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void AddAssembly_AddsNewAssembly()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                bool result = settings.AddAssembly("CustomAssembly");

                Assert.That(result, Is.True);
                Assert.That(settings.AdditionalAssemblyNames, Contains.Item("CustomAssembly"));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void AddAssembly_RejectsDefaultAssembly()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                bool result = settings.AddAssembly(UnityCodeMcpServerSettings.DefaultAssemblyNames[0]);

                Assert.That(result, Is.False);
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void AddAssembly_HandlesNullOrWhitespace()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                Assert.That(settings.AddAssembly(null), Is.False);
                Assert.That(settings.AddAssembly(string.Empty), Is.False);
                Assert.That(settings.AddAssembly("   "), Is.False);
                Assert.That(settings.AdditionalAssemblyNames, Is.Empty);
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void RemoveAssembly_RemovesExistingAssembly()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                settings.AdditionalAssemblyNames.Add("CustomAssembly");

                bool result = settings.RemoveAssembly("CustomAssembly");

                Assert.That(result, Is.True);
                Assert.That(settings.AdditionalAssemblyNames, Does.Not.Contain("CustomAssembly"));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void RemoveAssembly_ReturnsFalseWhenAssemblyMissing()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                bool result = settings.RemoveAssembly("NonExistentAssembly");

                Assert.That(result, Is.False);
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void RemoveAssembly_HandlesNullOrWhitespace()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                Assert.That(settings.RemoveAssembly(null), Is.False);
                Assert.That(settings.RemoveAssembly(string.Empty), Is.False);
                Assert.That(settings.RemoveAssembly("   "), Is.False);
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        #region Asset Creation Flow Tests

        private const string TestSettingsAssetPath = "Assets/Tests/EditMode/TestResources/TestUnityCodeMcpServerSettings.asset";

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // Use test-specific path for all tests
            UnityCodeMcpServerSettings.SetAssetPathForTesting(TestSettingsAssetPath);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            // Reset to default path and clean up test asset
            DeleteTestAsset();
            UnityCodeMcpServerSettings.ResetAssetPath();
        }

        [Test]
        public void SaveInstance_CreatesAssetWhenNotExists()
        {
            // Ensure asset doesn't exist
            DeleteTestAsset();

            UnityCodeMcpServerSettings testInstance = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            // Call SaveInstance
            UnityCodeMcpServerSettings.SaveInstance(testInstance);

            try
            {
                // Verify asset was created
                Assert.That(File.Exists(TestSettingsAssetPath), Is.True, "Asset file should be created");

                // Verify asset can be loaded
                UnityCodeMcpServerSettings loadedAsset = AssetDatabase.LoadAssetAtPath<UnityCodeMcpServerSettings>(TestSettingsAssetPath);
                Assert.That(loadedAsset, Is.Not.Null, "Asset should be loadable from database");
            }
            finally
            {
                // testInstance is now an asset, just delete the asset file
                DeleteTestAsset();
            }
        }

        [Test]
        public void SaveInstance_DoesNotOverwriteExistingAsset()
        {
            // Ensure asset doesn't exist
            DeleteTestAsset();

            UnityCodeMcpServerSettings firstInstance = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();
            firstInstance.LogToFile = false;

            // Create first asset
            UnityCodeMcpServerSettings.SaveInstance(firstInstance);

            try
            {
                // Modify the loaded asset
                UnityCodeMcpServerSettings loadedAsset = AssetDatabase.LoadAssetAtPath<UnityCodeMcpServerSettings>(TestSettingsAssetPath);
                loadedAsset.LogToFile = true;
                EditorUtility.SetDirty(loadedAsset);
                AssetDatabase.SaveAssets();

                // Try to save a different instance
                UnityCodeMcpServerSettings secondInstance = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();
                secondInstance.LogToFile = false;
                UnityCodeMcpServerSettings.SaveInstance(secondInstance);

                // Verify original asset was not overwritten
                UnityCodeMcpServerSettings reloadedAsset = AssetDatabase.LoadAssetAtPath<UnityCodeMcpServerSettings>(TestSettingsAssetPath);
                Assert.That(reloadedAsset.LogToFile, Is.True, "Existing asset should not be overwritten");

                // Second instance was not saved, can be destroyed
                ScriptableObject.DestroyImmediate(secondInstance);
            }
            finally
            {
                // Both instances are either assets or destroyed
                DeleteTestAsset();
            }
        }

        [Test]
        public void SaveInstance_HandlesNullInstance()
        {
            // Should not throw, just log warning
            Assert.DoesNotThrow(() => UnityCodeMcpServerSettings.SaveInstance(null));
        }

        [Test]
        public void SaveInstance_CreatesDirectoryIfNeeded()
        {
            // Delete the entire directory
            string directoryPath = Path.GetDirectoryName(TestSettingsAssetPath);
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
                AssetDatabase.Refresh();
            }

            UnityCodeMcpServerSettings testInstance = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            // Call SaveInstance - should create directory
            UnityCodeMcpServerSettings.SaveInstance(testInstance);

            try
            {
                // Verify directory was created
                Assert.That(Directory.Exists(directoryPath), Is.True, "Directory should be created");

                // Verify asset was created
                Assert.That(File.Exists(TestSettingsAssetPath), Is.True, "Asset should be created");
            }
            finally
            {
                // testInstance is now an asset, just delete the asset file
                DeleteTestAsset();
            }
        }

        [Test]
        public void GetOrCreateSettingsAsset_ReturnsExistingAsset()
        {
            // Ensure clean state
            DeleteTestAsset();

            // Create an asset first
            UnityCodeMcpServerSettings originalAsset = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();
            originalAsset.LogToFile = true;
            UnityCodeMcpServerSettings.SaveInstance(originalAsset);
            // originalAsset is now an asset, don't hold reference

            try
            {
                // Call GetOrCreateSettingsAsset
                UnityCodeMcpServerSettings retrievedAsset = UnityCodeMcpServerSettings.GetOrCreateSettingsAsset();

                // Verify it returned the existing asset
                Assert.That(retrievedAsset, Is.Not.Null);
                Assert.That(retrievedAsset.LogToFile, Is.True, "Should return existing asset with same values");
            }
            finally
            {
                DeleteTestAsset();
            }
        }

        [Test]
        public void GetOrCreateSettingsAsset_CreatesAssetWhenNotExists()
        {
            // Ensure asset doesn't exist
            DeleteTestAsset();

            try
            {
                // Call GetOrCreateSettingsAsset
                UnityCodeMcpServerSettings asset = UnityCodeMcpServerSettings.GetOrCreateSettingsAsset();

                // Verify asset was created
                Assert.That(asset, Is.Not.Null);
                Assert.That(File.Exists(TestSettingsAssetPath), Is.True, "Asset file should be created");

                // Verify it has default values
                Assert.That(asset.LogToFile, Is.False, "Should have default log-to-file value");
                Assert.That(asset.SkillsInstallTarget, Is.EqualTo(UnityCodeMcpServerSettings.SkillInstallTarget.Agents));
                Assert.That(asset.SkillsTargetPath, Is.EqualTo(".agents/skills/"));

                string diskContent = File.ReadAllText(TestSettingsAssetPath);
                Assert.That(diskContent, Does.Contain("SkillsInstallTarget: 2"));
                Assert.That(diskContent, Does.Contain("SkillsTargetPath: .agents/skills/"));

                ResetInstanceCache();
                UnityCodeMcpServerSettings reloadedAsset = UnityCodeMcpServerSettings.Instance;
                Assert.That(reloadedAsset.SkillsInstallTarget, Is.EqualTo(UnityCodeMcpServerSettings.SkillInstallTarget.Agents));
                Assert.That(reloadedAsset.SkillsTargetPath, Is.EqualTo(".agents/skills/"));
            }
            finally
            {
                ResetInstanceCache();
                DeleteTestAsset();
            }
        }

        [Test]
        public void Instance_ReturnsSameCachedInstance()
        {
            // Clear the cached instance using reflection
            ResetInstanceCache();
            DeleteTestAsset();

            try
            {
                // Get instance twice
                UnityCodeMcpServerSettings firstCall = UnityCodeMcpServerSettings.Instance;
                UnityCodeMcpServerSettings secondCall = UnityCodeMcpServerSettings.Instance;

                // Verify they are the same object
                Assert.That(ReferenceEquals(firstCall, secondCall), Is.True, "Instance should be cached and return same object");
            }
            finally
            {
                ResetInstanceCache();
                DeleteTestAsset();
            }
        }

        [Test]
        public void Instance_CreatesAssetOnFirstAccess()
        {
            // Clear cache and delete asset
            ResetInstanceCache();
            DeleteTestAsset();

            try
            {
                // Access Instance
                UnityCodeMcpServerSettings instance = UnityCodeMcpServerSettings.Instance;

                // Verify asset was created
                Assert.That(instance, Is.Not.Null);
                Assert.That(File.Exists(TestSettingsAssetPath), Is.True, "Asset should be created on first access");
            }
            finally
            {
                ResetInstanceCache();
                DeleteTestAsset();
            }
        }

        [Test]
        public void ShowSettings_SelectsOrCreatesSettingsAsset()
        {
            // ShowSettings finds any existing settings asset or creates one
            // It uses AssetDatabase.FindAssets which may find production or test assets

            // Call ShowSettings
            UnityCodeMcpServerSettings.ShowSettings();

            // Verify an asset is selected
            Assert.That(Selection.activeObject, Is.Not.Null, "ShowSettings should select an asset");
            Assert.That(Selection.activeObject, Is.InstanceOf<UnityCodeMcpServerSettings>(),
                "Selected object should be UnityCodeMcpServerSettings");
        }

        [Test]
        public void MinLogLevel_HasCorrectDefaultValue()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                Assert.That(settings.MinLogLevel, Is.EqualTo(UnityCodeMcpServer.Helpers.UnityCodeMcpServerLogger.LogLevel.Info),
                    "Default MinLogLevel should be Info");
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void InitializeSkillsTarget_UsesAgentsPreset_WhenUnset()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                settings.SkillsTargetPath = string.Empty;

                settings.InitializeSkillsTarget();

                Assert.That(settings.SkillsInstallTarget, Is.EqualTo(UnityCodeMcpServerSettings.SkillInstallTarget.Agents));
                StringAssert.Contains(".agents/skills", settings.SkillsTargetPath.Replace('\\', '/'));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void InitializeSkillsTarget_MigratesLegacyAbsolutePath_ToCustom()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                settings.SkillsTargetPath = "C:/tools/custom-skills";

                settings.InitializeSkillsTarget();

                Assert.That(settings.SkillsInstallTarget, Is.EqualTo(UnityCodeMcpServerSettings.SkillInstallTarget.Custom));
                Assert.That(settings.SkillsTargetPath, Is.EqualTo("C:/tools/custom-skills"));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void GetEffectiveSkillsTargetPath_ReturnsPresetPath()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                settings.SkillsInstallTarget = UnityCodeMcpServerSettings.SkillInstallTarget.GitHub;

                string path = settings.GetEffectiveSkillsTargetPath();

                StringAssert.EndsWith(".github/skills/", path.Replace('\\', '/'));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void GetEffectiveSkillsTargetPath_ReturnsCustomPath()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                settings.SkillsInstallTarget = UnityCodeMcpServerSettings.SkillInstallTarget.Custom;
                settings.SkillsTargetPath = "C:/skills/custom";

                Assert.That(settings.GetEffectiveSkillsTargetPath(), Is.EqualTo("C:/skills/custom"));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void SetSkillsInstallTarget_UpdatesStoredPath_ForPresetTarget()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                settings.SetSkillsInstallTarget(UnityCodeMcpServerSettings.SkillInstallTarget.Claude);

                Assert.That(settings.SkillsInstallTarget, Is.EqualTo(UnityCodeMcpServerSettings.SkillInstallTarget.Claude));
                StringAssert.EndsWith(".claude/skills/", settings.SkillsTargetPath.Replace('\\', '/'));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void SetSkillsInstallTarget_PreservesCustomPath_WhenSelectingCustom()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                settings.SkillsTargetPath = "C:/skills/custom";
                settings.SetSkillsInstallTarget(UnityCodeMcpServerSettings.SkillInstallTarget.Custom);

                Assert.That(settings.SkillsInstallTarget, Is.EqualTo(UnityCodeMcpServerSettings.SkillInstallTarget.Custom));
                Assert.That(settings.SkillsTargetPath, Is.EqualTo("C:/skills/custom"));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void SetSkillsInstallTarget_SeedsCustomPath_FromCurrentResolvedPresetPath()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                settings.SetSkillsInstallTarget(UnityCodeMcpServerSettings.SkillInstallTarget.GitHub);
                string expectedCustomPath = settings.GetEffectiveSkillsTargetPath();

                settings.SetSkillsInstallTarget(UnityCodeMcpServerSettings.SkillInstallTarget.Custom);

                Assert.That(settings.SkillsInstallTarget, Is.EqualTo(UnityCodeMcpServerSettings.SkillInstallTarget.Custom));
                Assert.That(settings.SkillsTargetPath, Is.EqualTo(expectedCustomPath));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void Instance_LoadsExistingAssetFromDisk()
        {
            // Arrange: create and save an asset with a non-default port
            ResetInstanceCache();
            DeleteTestAsset();

            UnityCodeMcpServerSettings original = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();
            original.LogToFile = true;
            UnityCodeMcpServerSettings.SaveInstance(original);

            // Clear the cache so Instance has to re-initialize
            ResetInstanceCache();

            try
            {
                // Act: access Instance
                UnityCodeMcpServerSettings instance = UnityCodeMcpServerSettings.Instance;

                // Assert: should have loaded the saved asset, not a brand-new default one
                Assert.That(instance.LogToFile, Is.True,
                    "Instance should load the existing asset from disk, not create a new default ScriptableObject");
            }
            finally
            {
                ResetInstanceCache();
                DeleteTestAsset();
            }
        }

        #endregion

        #region Searchable Assembly Dropdown Tests

        [Test]
        public void AssemblySearchableDropdown_BuildRoot_ContainsProvidedAssemblies()
        {
            object dropdown = CreateAssemblySearchableDropdown(
                new[] { "Alpha.Assembly", "Beta.Assembly" },
                _ => { });

            AdvancedDropdownItem root = BuildAssemblyDropdownRoot(dropdown);

            Assert.That(root.name, Is.EqualTo("Assemblies"));
            Assert.That(root.children.Count, Is.EqualTo(2));
            Assert.That(root.children.Select(child => child.name), Is.EqualTo(new[] { "Alpha.Assembly", "Beta.Assembly" }));
        }

        [Test]
        public void AssemblySearchableDropdown_ItemSelected_InvokesCallbackWithAssemblyName()
        {
            string selectedAssemblyName = null;
            object dropdown = CreateAssemblySearchableDropdown(
                new[] { "Alpha.Assembly", "Beta.Assembly" },
                assemblyName => selectedAssemblyName = assemblyName);

            AdvancedDropdownItem root = BuildAssemblyDropdownRoot(dropdown);
            SelectAssemblyDropdownItem(dropdown, root.children.ElementAt(1));

            Assert.That(selectedAssemblyName, Is.EqualTo("Beta.Assembly"));
        }

        [Test]
        public void HandleAssemblySelected_AddsAssemblyAndRefreshesAvailableAssemblies()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();
            UnityCodeMcpServerSettingsEditor inspector = null;

            try
            {
                string assemblyName = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetName().Name)
                    .FirstOrDefault(name =>
                        !string.IsNullOrWhiteSpace(name) &&
                        !UnityCodeMcpServerSettings.DefaultAssemblyNames.Contains(name));

                if (assemblyName == null)
                {
                    Assert.Ignore("No non-default assemblies are available in the current AppDomain.");
                }

                inspector = (UnityCodeMcpServerSettingsEditor)UnityEditor.Editor.CreateEditor(
                    settings,
                    typeof(UnityCodeMcpServerSettingsEditor));

                MethodInfo handleSelectionMethod = typeof(UnityCodeMcpServerSettingsEditor)
                    .GetMethod("HandleAssemblySelected", BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.That(handleSelectionMethod, Is.Not.Null);

                bool wasAdded = (bool)handleSelectionMethod.Invoke(inspector, new object[] { assemblyName });

                Assert.That(wasAdded, Is.True);
                Assert.That(settings.AdditionalAssemblyNames, Contains.Item(assemblyName));

                string[] availableAssemblies = (string[])typeof(UnityCodeMcpServerSettingsEditor)
                    .GetField("_availableAssemblyNames", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(inspector);

                Assert.That(availableAssemblies, Does.Not.Contain(assemblyName));
            }
            finally
            {
                if (inspector != null)
                {
                    UnityEngine.Object.DestroyImmediate(inspector);
                }

                ScriptableObject.DestroyImmediate(settings);
            }
        }

        #endregion

        #region OnInspectorGUI Persistence Tests

        /// <summary>
        /// Minimal EditorWindow that delegates OnGUI to a given custom editor.
        /// Used to provide a real IMGUI context so OnInspectorGUI can be exercised in tests.
        /// </summary>
        private class SettingsEditorTestWindow : EditorWindow
        {
            public UnityCodeMcpServer.Settings.Editor.UnityCodeMcpServerSettingsEditor Inspector;

            private void OnGUI()
            {
                Inspector?.OnInspectorGUI();
            }
        }

        [UnityTest]
        public IEnumerator OnInspectorGUI_SavesDirtySettingsToDisk()
        {
            // Arrange: create a settings asset on disk with a known logging value
            DeleteTestAsset();
            UnityCodeMcpServerSettings initial = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();
            initial.LogToFile = false;
            UnityCodeMcpServerSettings.SaveInstance(initial);

            UnityCodeMcpServerSettings loadedSettings = AssetDatabase.LoadAssetAtPath<UnityCodeMcpServerSettings>(TestSettingsAssetPath);
            Assert.That(loadedSettings, Is.Not.Null);

            // Act: change the log setting and mark the asset dirty, then let the inspector run
            loadedSettings.LogToFile = true;
            EditorUtility.SetDirty(loadedSettings);

            UnityCodeMcpServerSettingsEditor inspector = (UnityCodeMcpServer.Settings.Editor.UnityCodeMcpServerSettingsEditor)
                UnityEditor.Editor.CreateEditor(
                    loadedSettings,
                    typeof(UnityCodeMcpServer.Settings.Editor.UnityCodeMcpServerSettingsEditor));

            // Open an EditorWindow whose OnGUI calls inspector.OnInspectorGUI().
            // This is necessary because IMGUI functions require a real OnGUI context.
            SettingsEditorTestWindow window = EditorWindow.GetWindow<SettingsEditorTestWindow>("Settings Editor Test");
            window.Inspector = inspector;
            window.Repaint();

            // Yield one editor frame so OnGUI fires and AssetDatabase.SaveAssetIfDirty runs
            yield return null;

            window.Close();
            UnityEngine.Object.DestroyImmediate(inspector);

            // Assert: the .asset file on disk must now contain the updated value
            string diskContent = File.ReadAllText(TestSettingsAssetPath);
            Assert.That(diskContent, Does.Contain("LogToFile: 1"),
                "Settings changes should be flushed to disk by OnInspectorGUI via AssetDatabase.SaveAssetIfDirty");

            DeleteTestAsset();
        }

        #endregion

        #region Helper Methods

        private void DeleteTestAsset()
        {
            if (File.Exists(TestSettingsAssetPath))
            {
                AssetDatabase.DeleteAsset(TestSettingsAssetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private void ResetInstanceCache()
        {
            // Use reflection to reset the cached _instance field
            FieldInfo instanceField = typeof(UnityCodeMcpServerSettings)
                .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);

            if (instanceField != null)
            {
                // Just clear the reference, don't try to destroy
                // If it's an asset, Unity manages it; if not, it will be GC'd
                instanceField.SetValue(null, null);
            }
        }

        private static object CreateAssemblySearchableDropdown(string[] assemblyNames, Action<string> onSelected)
        {
            Type dropdownType = typeof(UnityCodeMcpServerSettingsEditor)
                .GetNestedType("AssemblySearchableDropdown", BindingFlags.NonPublic);

            Assert.That(dropdownType, Is.Not.Null, "Expected searchable dropdown type to exist.");
            Assert.That(typeof(AdvancedDropdown).IsAssignableFrom(dropdownType), Is.True);

            return Activator.CreateInstance(
                dropdownType,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                binder: null,
                args: new object[] { new AdvancedDropdownState(), assemblyNames, onSelected },
                culture: null);
        }

        private static AdvancedDropdownItem BuildAssemblyDropdownRoot(object dropdown)
        {
            MethodInfo buildRootMethod = dropdown.GetType()
                .GetMethod("BuildRoot", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(buildRootMethod, Is.Not.Null);
            return (AdvancedDropdownItem)buildRootMethod.Invoke(dropdown, null);
        }

        private static void SelectAssemblyDropdownItem(object dropdown, AdvancedDropdownItem item)
        {
            MethodInfo itemSelectedMethod = dropdown.GetType()
                .GetMethod("ItemSelected", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(itemSelectedMethod, Is.Not.Null);
            itemSelectedMethod.Invoke(dropdown, new object[] { item });
        }

        #endregion
    }
}
