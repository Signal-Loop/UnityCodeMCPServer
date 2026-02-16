using NUnit.Framework;

namespace UnityCodeMcpServer.Tests.EditMode
{
    /// <summary>
    /// Dummy EditMode test to verify the Unity Test Runner is executing EditMode tests.
    /// </summary>
    public class DummyEditModeTests
    {
        [Test]
        public void EditMode_TestRunner_Executes()
        {
            Assert.IsTrue(true, "EditMode test runner executed the test.");
        }
    }
}
