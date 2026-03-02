using System.Reflection;
using System.Linq;
using System.IO;
using UnityCodeMcpServer.Settings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UnityCodeMcpServer.Tests.EditMode
{
    [TestFixture]
    public class UnityCodeMcpServerSettingsTests
    {
        private static void InvokeOnValidate(UnityCodeMcpServerSettings settings)
        {
            var method = typeof(UnityCodeMcpServerSettings)
                .GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(settings, null);
        }

        [SetUp]
        public void SetUp()
        {
            ServerLifecycleCoordinator.SetHandlers(
                startTcp: () => { },
                stopTcp: () => { },
                restartTcp: () => { },
                startHttp: () => { },
                stopHttp: () => { },
                restartHttp: () => { });
        }

        [TearDown]
        public void TearDown()
        {
            ServerLifecycleCoordinator.ResetHandlers();
        }

        [Test]
        public void DefaultStartupServer_IsStdio()
        {
            var settings = UnityCodeMcpServerSettings.Instance;
            Assert.That(settings.StartupServer, Is.EqualTo(UnityCodeMcpServerSettings.ServerStartupMode.Stdio));
        }

        [Test]
        public void StartupServer_CanBeSetToHttp()
        {
            var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                settings.StartupServer = UnityCodeMcpServerSettings.ServerStartupMode.Http;

                Assert.That(settings.StartupServer, Is.EqualTo(UnityCodeMcpServerSettings.ServerStartupMode.Http));
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
            var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                var allAssemblies = settings.GetAllAssemblyNames();

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
            var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                settings.AdditionalAssemblyNames.Add("CustomAssembly1");
                settings.AdditionalAssemblyNames.Add("CustomAssembly2");

                var allAssemblies = settings.GetAllAssemblyNames();

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
            var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                settings.AdditionalAssemblyNames.Add("CustomAssembly");
                settings.AdditionalAssemblyNames.Add("CustomAssembly"); // Duplicate

                var allAssemblies = settings.GetAllAssemblyNames();

                Assert.That(allAssemblies.Count(a => a == "CustomAssembly"), Is.EqualTo(1));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void AddAssembly_AddsNewAssembly()
        {
            var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                var result = settings.AddAssembly("NewCustomAssembly");

                Assert.That(result, Is.True);
                Assert.That(settings.AdditionalAssemblyNames, Contains.Item("NewCustomAssembly"));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void AddAssembly_RejectsDuplicates()
        {
            var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                settings.AddAssembly("TestAssembly");
                var result = settings.AddAssembly("TestAssembly");

                Assert.That(result, Is.False);
                Assert.That(settings.AdditionalAssemblyNames.Count(a => a == "TestAssembly"), Is.EqualTo(1));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void AddAssembly_RejectsDefaultAssemblies()
        {
            var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                var result = settings.AddAssembly("Assembly-CSharp");

                Assert.That(result, Is.False);
                Assert.That(settings.AdditionalAssemblyNames, Does.Not.Contains("Assembly-CSharp"));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void AddAssembly_RejectsNullOrWhitespace()
        {
            var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                Assert.That(settings.AddAssembly(null), Is.False);
                Assert.That(settings.AddAssembly(""), Is.False);
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
            var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                settings.AdditionalAssemblyNames.Add("TestAssembly");

                var result = settings.RemoveAssembly("TestAssembly");

                Assert.That(result, Is.True);
                Assert.That(settings.AdditionalAssemblyNames, Does.Not.Contains("TestAssembly"));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void RemoveAssembly_ReturnsFalseForNonExistent()
        {
            var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                var result = settings.RemoveAssembly("NonExistentAssembly");

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
            var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                Assert.That(settings.RemoveAssembly(null), Is.False);
                Assert.That(settings.RemoveAssembly(""), Is.False);
                Assert.That(settings.RemoveAssembly("   "), Is.False);
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void OnValidate_PortChangeWithStdioSelection_RestartsTcp()
        {
            var restartCount = 0;
            ServerLifecycleCoordinator.SetHandlers(
                startTcp: () => { },
                stopTcp: () => { },
                restartTcp: () => restartCount++,
                startHttp: () => { },
                stopHttp: () => { },
                restartHttp: () => { });

            var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                settings.StartupServer = UnityCodeMcpServerSettings.ServerStartupMode.Stdio;
                settings.StdioPort = 21088;
                InvokeOnValidate(settings);

                settings.StdioPort = 21099;
                InvokeOnValidate(settings);

                Assert.That(restartCount, Is.EqualTo(1));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void OnValidate_PortChangeWithHttpSelection_DoesNotRestartTcp()
        {
            var restartCount = 0;
            ServerLifecycleCoordinator.SetHandlers(
                startTcp: () => { },
                stopTcp: () => { },
                restartTcp: () => restartCount++,
                startHttp: () => { },
                stopHttp: () => { },
                restartHttp: () => { });

            var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                settings.StartupServer = UnityCodeMcpServerSettings.ServerStartupMode.Http;
                settings.StdioPort = 21088;
                InvokeOnValidate(settings);

                settings.StdioPort = 21100;
                InvokeOnValidate(settings);

                Assert.That(restartCount, Is.EqualTo(0));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void OnValidate_HttpPortChangeWithHttpSelection_RestartsHttp()
        {
            var restartHttpCount = 0;
            ServerLifecycleCoordinator.SetHandlers(
                startTcp: () => { },
                stopTcp: () => { },
                restartTcp: () => { },
                startHttp: () => { },
                stopHttp: () => { },
                restartHttp: () => restartHttpCount++);

            var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                settings.StartupServer = UnityCodeMcpServerSettings.ServerStartupMode.Http;
                settings.HttpPort = 3001;
                InvokeOnValidate(settings);

                settings.HttpPort = 3002;
                InvokeOnValidate(settings);

                Assert.That(restartHttpCount, Is.EqualTo(1));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void OnValidate_HttpPortChangeWithStdioSelection_DoesNotRestartHttp()
        {
            var restartHttpCount = 0;
            ServerLifecycleCoordinator.SetHandlers(
                startTcp: () => { },
                stopTcp: () => { },
                restartTcp: () => { },
                startHttp: () => { },
                stopHttp: () => { },
                restartHttp: () => restartHttpCount++);

            var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                settings.StartupServer = UnityCodeMcpServerSettings.ServerStartupMode.Stdio;
                settings.HttpPort = 3001;
                InvokeOnValidate(settings);

                settings.HttpPort = 3003;
                InvokeOnValidate(settings);

                Assert.That(restartHttpCount, Is.EqualTo(0));
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

            var testInstance = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            // Call SaveInstance
            UnityCodeMcpServerSettings.SaveInstance(testInstance);

            try
            {
                // Verify asset was created
                Assert.That(File.Exists(TestSettingsAssetPath), Is.True, "Asset file should be created");

                // Verify asset can be loaded
                var loadedAsset = AssetDatabase.LoadAssetAtPath<UnityCodeMcpServerSettings>(TestSettingsAssetPath);
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

            var firstInstance = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();
            firstInstance.StdioPort = 12345;

            // Create first asset
            UnityCodeMcpServerSettings.SaveInstance(firstInstance);

            try
            {
                // Modify the loaded asset
                var loadedAsset = AssetDatabase.LoadAssetAtPath<UnityCodeMcpServerSettings>(TestSettingsAssetPath);
                loadedAsset.StdioPort = 99999;
                EditorUtility.SetDirty(loadedAsset);
                AssetDatabase.SaveAssets();

                // Try to save a different instance
                var secondInstance = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();
                secondInstance.StdioPort = 54321;
                UnityCodeMcpServerSettings.SaveInstance(secondInstance);

                // Verify original asset was not overwritten
                var reloadedAsset = AssetDatabase.LoadAssetAtPath<UnityCodeMcpServerSettings>(TestSettingsAssetPath);
                Assert.That(reloadedAsset.StdioPort, Is.EqualTo(99999), "Existing asset should not be overwritten");

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
            var directoryPath = Path.GetDirectoryName(TestSettingsAssetPath);
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
                AssetDatabase.Refresh();
            }

            var testInstance = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

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
            var originalAsset = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();
            originalAsset.StdioPort = 77777;
            UnityCodeMcpServerSettings.SaveInstance(originalAsset);
            // originalAsset is now an asset, don't hold reference

            try
            {
                // Call GetOrCreateSettingsAsset
                var retrievedAsset = UnityCodeMcpServerSettings.GetOrCreateSettingsAsset();

                // Verify it returned the existing asset
                Assert.That(retrievedAsset, Is.Not.Null);
                Assert.That(retrievedAsset.StdioPort, Is.EqualTo(77777), "Should return existing asset with same values");
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
                var asset = UnityCodeMcpServerSettings.GetOrCreateSettingsAsset();

                // Verify asset was created
                Assert.That(asset, Is.Not.Null);
                Assert.That(File.Exists(TestSettingsAssetPath), Is.True, "Asset file should be created");

                // Verify it has default values
                Assert.That(asset.StdioPort, Is.EqualTo(21088), "Should have default port value");
            }
            finally
            {
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
                var firstCall = UnityCodeMcpServerSettings.Instance;
                var secondCall = UnityCodeMcpServerSettings.Instance;

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
                var instance = UnityCodeMcpServerSettings.Instance;

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
            var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                Assert.That(settings.MinLogLevel, Is.EqualTo(UnityCodeMcpServer.Helpers.LoopLogger.LogLevel.Info),
                    "Default MinLogLevel should be Info");
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

            var original = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();
            original.StdioPort = 55555;
            UnityCodeMcpServerSettings.SaveInstance(original);

            // Clear the cache so Instance has to re-initialize
            ResetInstanceCache();

            try
            {
                // Act: access Instance
                var instance = UnityCodeMcpServerSettings.Instance;

                // Assert: should have loaded the saved asset, not a brand-new default one
                Assert.That(instance.StdioPort, Is.EqualTo(55555),
                    "Instance should load the existing asset from disk, not create a new default ScriptableObject");
            }
            finally
            {
                ResetInstanceCache();
                DeleteTestAsset();
            }
        }

        [Test]
        public void OnValidate_LogsPortFromThisNotFromInstance()
        {
            // Arrange: save an asset with port 11111 so Instance would return that if accessed
            ResetInstanceCache();
            DeleteTestAsset();

            var assetInstance = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();
            assetInstance.StdioPort = 11111;
            UnityCodeMcpServerSettings.SaveInstance(assetInstance);
            ResetInstanceCache();

            // Create a separate in-memory settings with a different port
            var inMemorySettings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();
            inMemorySettings.StdioPort = 22222;

            try
            {
                // If OnValidate reads from `this`, the logged port should reflect inMemorySettings.StdioPort.
                // If it incorrectly reads from `Instance`, it would see 11111 (from the asset).
                // We verify indirectly: after OnValidate the port on `this` object is unchanged.
                InvokeOnValidate(inMemorySettings);

                Assert.That(inMemorySettings.StdioPort, Is.EqualTo(22222),
                    "OnValidate must not overwrite 'this' port by reading from Instance");

                // Also confirm Instance has loaded the asset (port 11111), not the in-memory object
                Assert.That(UnityCodeMcpServerSettings.Instance.StdioPort, Is.EqualTo(11111),
                    "Instance should be the saved asset, independent from the in-memory settings object");
            }
            finally
            {
                ScriptableObject.DestroyImmediate(inMemorySettings);
                ResetInstanceCache();
                DeleteTestAsset();
            }
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
            var instanceField = typeof(UnityCodeMcpServerSettings)
                .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);

            if (instanceField != null)
            {
                // Just clear the reference, don't try to destroy
                // If it's an asset, Unity manages it; if not, it will be GC'd
                instanceField.SetValue(null, null);
            }
        }

        #endregion
    }
}
