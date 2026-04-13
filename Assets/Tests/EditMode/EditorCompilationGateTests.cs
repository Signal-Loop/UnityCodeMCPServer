using NUnit.Framework;
using UnityCodeMcpServer.Helpers;

namespace UnityCodeMcpServer.Tests.EditMode
{
    public class EditorCompilationGateTests
    {
        [Test]
        public void ShouldBlock_WhenEditorIsCompiling()
        {
            var shouldBlock = EditorCompilationGate.ShouldBlock(isCompiling: true, hasCompileErrors: false);

            Assert.IsTrue(shouldBlock);
        }

        [Test]
        public void ShouldBlock_WhenCompileErrorsExist()
        {
            var shouldBlock = EditorCompilationGate.ShouldBlock(isCompiling: false, hasCompileErrors: true);

            Assert.IsTrue(shouldBlock);
        }

        [Test]
        public void ShouldNotBlock_WhenEditorIsReady()
        {
            var shouldBlock = EditorCompilationGate.ShouldBlock(isCompiling: false, hasCompileErrors: false);

            Assert.IsFalse(shouldBlock);
        }

        [Test]
        public void BuildBlockedMessage_ForCompilingState_UsesActionName()
        {
            var message = EditorCompilationGate.BuildBlockedMessage("execute C# scripts", isCompiling: true, hasCompileErrors: false);

            Assert.That(message, Does.Contain("Cannot execute C# scripts while the editor is compiling scripts"));
        }

        [Test]
        public void BuildBlockedMessage_ForCompilerErrors_UsesActionName()
        {
            var message = EditorCompilationGate.BuildBlockedMessage("run Unity tests", isCompiling: false, hasCompileErrors: true);

            Assert.That(message, Does.Contain("Cannot run Unity tests while the project has compiler errors"));
        }
    }
}