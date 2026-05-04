using NUnit.Framework;
using UnityCodeMcpServer.Settings;
using UnityEngine;

namespace UnityCodeMcpServer.Tests.EditMode
{
    [TestFixture]
    public class ServerLifecycleCoordinatorTests
    {
        private int _startServerCount;
        private int _restartServerCount;

        [SetUp]
        public void SetUp()
        {
            _startServerCount = 0;
            _restartServerCount = 0;

            ServerLifecycleCoordinator.SetHandlers(
                startServer: () => _startServerCount++,
                restartServer: () => _restartServerCount++);
        }

        [TearDown]
        public void TearDown()
        {
            ServerLifecycleCoordinator.ResetHandlers();
        }

        [Test]
        public void UpdateServerState_StartsFileServer()
        {
            ServerLifecycleCoordinator.UpdateServerState();

            Assert.That(_startServerCount, Is.EqualTo(1));
            Assert.That(_restartServerCount, Is.EqualTo(0));
        }

        [Test]
        public void UpdateServerState_WithRestart_RestartsFileServer()
        {
            ServerLifecycleCoordinator.UpdateServerState(restartServer: true);

            Assert.That(_restartServerCount, Is.EqualTo(1));
            Assert.That(_startServerCount, Is.EqualTo(0));
        }

        [Test]
        public void UnityCodeMcpServerSettings_ApplySelection_StartsFileServer()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();
            try
            {
                settings.ApplySelection();

                Assert.That(_startServerCount, Is.EqualTo(1));
                Assert.That(_restartServerCount, Is.EqualTo(0));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }
    }
}
