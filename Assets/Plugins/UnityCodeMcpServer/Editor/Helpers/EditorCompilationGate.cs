using UnityEditor;

namespace UnityCodeMcpServer.Helpers
{
    public static class EditorCompilationGate
    {
        public static bool ShouldBlock(bool isCompiling, bool hasCompileErrors)
        {
            return isCompiling || hasCompileErrors;
        }

        public static bool TryGetBlockedMessage(string actionName, out string message)
        {
            bool isCompiling = EditorApplication.isCompiling;
            bool hasCompileErrors = EditorUtility.scriptCompilationFailed;

            if (!ShouldBlock(isCompiling, hasCompileErrors))
            {
                message = null;
                return false;
            }

            message = BuildBlockedMessage(actionName, isCompiling, hasCompileErrors);
            return true;
        }

        public static string BuildBlockedMessage(string actionName, bool isCompiling, bool hasCompileErrors)
        {
            if (!ShouldBlock(isCompiling, hasCompileErrors))
            {
                return null;
            }

            if (isCompiling)
            {
                return $"Cannot {actionName} while the editor is compiling scripts. Wait for compilation to finish and fix any compiler errors before retrying.";
            }

            return $"Cannot {actionName} while the project has compiler errors. Fix all compiler errors before retrying.";
        }
    }
}