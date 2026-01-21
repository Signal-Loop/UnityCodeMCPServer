using System.Reflection;
using System.Linq;
using UnityCodeMcpServer.Settings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UnityCodeMcpServer.Tests
{
    [TestFixture]
    public class UnityCodeMcpServerSettingsTests
    {
        [SetUp]
        public void SetUp()
        {
            ServerLifecycleCoordinator.SetHandlers(
                startTcp: () => { },
                stopTcp: () => { },
                startHttp: () => { },
                stopHttp: () => { });
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
    }
}
