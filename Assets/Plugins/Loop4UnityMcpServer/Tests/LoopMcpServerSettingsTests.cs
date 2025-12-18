using System.Reflection;
using System.Linq;
using LoopMcpServer.Settings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LoopMcpServer.Tests
{
    [TestFixture]
    public class LoopMcpServerSettingsTests
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
            var settings = LoopMcpServerSettings.Instance;
            Assert.That(settings.StartupServer, Is.EqualTo(LoopMcpServerSettings.ServerStartupMode.Stdio));
        }

        [Test]
        public void StartupServer_CanBeSetToHttp()
        {
            var settings = ScriptableObject.CreateInstance<LoopMcpServerSettings>();

            try
            {
                settings.StartupServer = LoopMcpServerSettings.ServerStartupMode.Http;

                Assert.That(settings.StartupServer, Is.EqualTo(LoopMcpServerSettings.ServerStartupMode.Http));
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
            LoopMcpServerSettings.ShowSettings();

            // Verify an asset was selected
            Assert.That(Selection.activeObject, Is.Not.Null);
            Assert.That(Selection.activeObject, Is.InstanceOf<LoopMcpServerSettings>());
        }

        [Test]
        public void DefaultAssemblyNames_ContainsExpectedAssemblies()
        {
            Assert.That(LoopMcpServerSettings.DefaultAssemblyNames, Contains.Item("System.Core"));
            Assert.That(LoopMcpServerSettings.DefaultAssemblyNames, Contains.Item("UnityEngine.CoreModule"));
            Assert.That(LoopMcpServerSettings.DefaultAssemblyNames, Contains.Item("Assembly-CSharp"));
            Assert.That(LoopMcpServerSettings.DefaultAssemblyNames, Contains.Item("Assembly-CSharp-Editor"));
        }

        [Test]
        public void GetAllAssemblyNames_ReturnsDefaultsWhenNoAdditional()
        {
            var settings = ScriptableObject.CreateInstance<LoopMcpServerSettings>();

            try
            {
                var allAssemblies = settings.GetAllAssemblyNames();

                Assert.That(allAssemblies, Is.EquivalentTo(LoopMcpServerSettings.DefaultAssemblyNames));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void GetAllAssemblyNames_CombinesDefaultAndAdditional()
        {
            var settings = ScriptableObject.CreateInstance<LoopMcpServerSettings>();

            try
            {
                settings.AdditionalAssemblyNames.Add("CustomAssembly1");
                settings.AdditionalAssemblyNames.Add("CustomAssembly2");

                var allAssemblies = settings.GetAllAssemblyNames();

                Assert.That(allAssemblies, Contains.Item("CustomAssembly1"));
                Assert.That(allAssemblies, Contains.Item("CustomAssembly2"));
                Assert.That(allAssemblies.Length, Is.EqualTo(LoopMcpServerSettings.DefaultAssemblyNames.Length + 2));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void GetAllAssemblyNames_RemovesDuplicates()
        {
            var settings = ScriptableObject.CreateInstance<LoopMcpServerSettings>();

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
            var settings = ScriptableObject.CreateInstance<LoopMcpServerSettings>();

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
            var settings = ScriptableObject.CreateInstance<LoopMcpServerSettings>();

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
            var settings = ScriptableObject.CreateInstance<LoopMcpServerSettings>();

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
            var settings = ScriptableObject.CreateInstance<LoopMcpServerSettings>();

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
            var settings = ScriptableObject.CreateInstance<LoopMcpServerSettings>();

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
            var settings = ScriptableObject.CreateInstance<LoopMcpServerSettings>();

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
            var settings = ScriptableObject.CreateInstance<LoopMcpServerSettings>();

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
