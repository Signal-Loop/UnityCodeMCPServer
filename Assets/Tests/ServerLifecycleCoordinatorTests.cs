using System.Reflection;
using UnityCodeMcpServer.Settings;
using NUnit.Framework;
using UnityEngine;

namespace UnityCodeMcpServer.Tests
{
    [TestFixture]
    public class ServerLifecycleCoordinatorTests
    {
        private int _startTcpCount;
        private int _stopTcpCount;
        private int _restartTcpCount;
        private int _startHttpCount;
        private int _stopHttpCount;
        private int _restartHttpCount;

        [SetUp]
        public void SetUp()
        {
            _startTcpCount = 0;
            _stopTcpCount = 0;
            _restartTcpCount = 0;
            _startHttpCount = 0;
            _stopHttpCount = 0;
            _restartHttpCount = 0;

            ServerLifecycleCoordinator.SetHandlers(
                startTcp: () => _startTcpCount++,
                stopTcp: () => _stopTcpCount++,
                restartTcp: () => _restartTcpCount++,
                startHttp: () => _startHttpCount++,
                stopHttp: () => _stopHttpCount++,
                restartHttp: () => _restartHttpCount++);
        }

        [TearDown]
        public void TearDown()
        {
            ServerLifecycleCoordinator.ResetHandlers();
        }

        [Test]
        public void ApplySelection_Stdio_StartsTcpStopsHttp()
        {
            ServerLifecycleCoordinator.UpdateServerState(UnityCodeMcpServerSettings.ServerStartupMode.Stdio);

            Assert.That(_startTcpCount, Is.EqualTo(1));
            Assert.That(_stopHttpCount, Is.EqualTo(1));
            Assert.That(_stopTcpCount, Is.EqualTo(0));
            Assert.That(_startHttpCount, Is.EqualTo(0));
            Assert.That(_restartTcpCount, Is.EqualTo(0));
            Assert.That(_restartHttpCount, Is.EqualTo(0));
        }

        [Test]
        public void ApplySelection_Http_StartsHttpStopsTcp()
        {
            ServerLifecycleCoordinator.UpdateServerState(UnityCodeMcpServerSettings.ServerStartupMode.Http);

            Assert.That(_startHttpCount, Is.EqualTo(1));
            Assert.That(_stopTcpCount, Is.EqualTo(1));
            Assert.That(_startTcpCount, Is.EqualTo(0));
            Assert.That(_stopHttpCount, Is.EqualTo(0));
            Assert.That(_restartTcpCount, Is.EqualTo(0));
            Assert.That(_restartHttpCount, Is.EqualTo(0));
        }

        [Test]
        public void ApplySelection_Stdio_WithRestart_RestartsTcp()
        {
            ServerLifecycleCoordinator.UpdateServerState(
                UnityCodeMcpServerSettings.ServerStartupMode.Stdio,
                restartTcp: true,
                restartHttp: false);

            Assert.That(_restartTcpCount, Is.EqualTo(1));
            Assert.That(_startTcpCount, Is.EqualTo(0));
            Assert.That(_stopHttpCount, Is.EqualTo(1));
            Assert.That(_restartHttpCount, Is.EqualTo(0));
        }

        [Test]
        public void ApplySelection_Http_WithRestart_RestartsHttp()
        {
            ServerLifecycleCoordinator.UpdateServerState(
                UnityCodeMcpServerSettings.ServerStartupMode.Http,
                restartTcp: false,
                restartHttp: true);

            Assert.That(_restartHttpCount, Is.EqualTo(1));
            Assert.That(_startHttpCount, Is.EqualTo(0));
            Assert.That(_stopTcpCount, Is.EqualTo(1));
            Assert.That(_restartTcpCount, Is.EqualTo(0));
        }

        [Test]
        public void UnityCodeMcpServerSettings_ApplySelection_InvokesCoordinator()
        {
            var settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();
            try
            {
                settings.StartupServer = UnityCodeMcpServerSettings.ServerStartupMode.Http;

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
