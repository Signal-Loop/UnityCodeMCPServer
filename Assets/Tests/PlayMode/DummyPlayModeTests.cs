using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;

namespace UnityCodeMcpServer.Tests.PlayMode
{
    /// <summary>
    /// Dummy PlayMode test to verify the Unity Test Runner is executing PlayMode tests.
    /// </summary>
    public class DummyPlayModeTests
    {
        [UnityTest]
        public IEnumerator PlayMode_TestRunner_Executes()
        {
            // Wait one frame to ensure test runner is in PlayMode
            yield return null;

            Assert.IsTrue(true, "PlayMode test runner executed the test.");
        }
    }
}
