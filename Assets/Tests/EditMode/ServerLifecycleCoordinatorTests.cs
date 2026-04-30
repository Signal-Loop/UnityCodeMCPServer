using NUnit.Framework;
using UnityCodeMcpServer.Settings;
using UnityEngine;

namespace UnityCodeMcpServer.Tests.EditMode
{
    [TestFixture]
    public class ServerLifecycleCoordinatorTests
    {
        private int _startHttpCount;
        private int _stopHttpCount;
        private int _restartHttpCount;

        [SetUp]
        public void SetUp()
        {
            _startHttpCount = 0;
            _stopHttpCount = 0;
            _restartHttpCount = 0;

            ServerLifecycleCoordinator.SetHandlers(
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
        public void UpdateServerState_StartsHttp()
        {
            ServerLifecycleCoordinator.UpdateServerState();

            Assert.That(_startHttpCount, Is.EqualTo(1));
            Assert.That(_restartHttpCount, Is.EqualTo(0));
            Assert.That(_stopHttpCount, Is.EqualTo(0));
        }

        [Test]
        public void UpdateServerState_WithRestart_RestartsHttp()
        {
            ServerLifecycleCoordinator.UpdateServerState(restartHttp: true);

            Assert.That(_restartHttpCount, Is.EqualTo(1));
            Assert.That(_startHttpCount, Is.EqualTo(0));
            Assert.That(_stopHttpCount, Is.EqualTo(0));
        }

        [Test]
        public void UnityCodeMcpServerSettings_ApplySelection_StartsHttpServer()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();
            try
            {
                settings.ApplySelection();

                Assert.That(_startHttpCount, Is.EqualTo(1));
                Assert.That(_restartHttpCount, Is.EqualTo(0));
                Assert.That(_stopHttpCount, Is.EqualTo(0));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }
    }
}
