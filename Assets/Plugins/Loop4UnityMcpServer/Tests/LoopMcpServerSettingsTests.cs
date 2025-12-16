using System.Reflection;
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
                var field = typeof(LoopMcpServerSettings).GetField("_startupServer", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(field, Is.Not.Null);

                field.SetValue(settings, LoopMcpServerSettings.ServerStartupMode.Http);

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
    }
}
