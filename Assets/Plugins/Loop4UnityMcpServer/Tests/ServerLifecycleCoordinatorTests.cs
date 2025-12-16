using System.Reflection;
using LoopMcpServer.Settings;
using NUnit.Framework;
using UnityEngine;

namespace LoopMcpServer.Tests
{
    [TestFixture]
    public class ServerLifecycleCoordinatorTests
    {
        private int _startTcpCount;
        private int _stopTcpCount;
        private int _startHttpCount;
        private int _stopHttpCount;

        [SetUp]
        public void SetUp()
        {
            _startTcpCount = 0;
            _stopTcpCount = 0;
            _startHttpCount = 0;
            _stopHttpCount = 0;

            ServerLifecycleCoordinator.SetHandlers(
                startTcp: () => _startTcpCount++,
                stopTcp: () => _stopTcpCount++,
                startHttp: () => _startHttpCount++,
                stopHttp: () => _stopHttpCount++);
        }

        [TearDown]
        public void TearDown()
        {
            ServerLifecycleCoordinator.ResetHandlers();
        }

        [Test]
        public void ApplySelection_Stdio_StartsTcpStopsHttp()
        {
            ServerLifecycleCoordinator.ApplySelection(LoopMcpServerSettings.ServerStartupMode.Stdio);

            Assert.That(_startTcpCount, Is.EqualTo(1));
            Assert.That(_stopHttpCount, Is.EqualTo(1));
            Assert.That(_stopTcpCount, Is.EqualTo(0));
            Assert.That(_startHttpCount, Is.EqualTo(0));
        }

        [Test]
        public void ApplySelection_Http_StartsHttpStopsTcp()
        {
            ServerLifecycleCoordinator.ApplySelection(LoopMcpServerSettings.ServerStartupMode.Http);

            Assert.That(_startHttpCount, Is.EqualTo(1));
            Assert.That(_stopTcpCount, Is.EqualTo(1));
            Assert.That(_startTcpCount, Is.EqualTo(0));
            Assert.That(_stopHttpCount, Is.EqualTo(0));
        }

        [Test]
        public void LoopMcpServerSettings_ApplySelection_InvokesCoordinator()
        {
            var settings = ScriptableObject.CreateInstance<LoopMcpServerSettings>();
            try
            {
                var field = typeof(LoopMcpServerSettings).GetField("_startupServer", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(field, Is.Not.Null);

                field.SetValue(settings, LoopMcpServerSettings.ServerStartupMode.Http);

                settings.ApplySelection();

                Assert.That(_startHttpCount, Is.EqualTo(1));
                Assert.That(_stopTcpCount, Is.EqualTo(1));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }
    }
}
