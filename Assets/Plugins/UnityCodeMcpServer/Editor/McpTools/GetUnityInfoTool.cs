using System.IO;
using System.Text;
using System.Text.Json;
using UnityCodeMcpServer.Interfaces;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Settings;
using UnityEngine;

namespace UnityCodeMcpServer.McpTools
{
    /// <summary>
    /// Tool that returns Unity Editor project information and current server settings.
    /// </summary>
    public class GetUnityInfoTool : ITool
    {
        public string Name => "get_unity_info";

        public string Description =>
            @"Returns information about the current Unity Editor project and the UnityCodeMcpServer settings.

**Returns:**
- `project_path`: The absolute path to the Unity project root directory.
- `settings`: The current UnityCodeMcpServerSettings values (HTTP server settings, logging, assemblies, etc.).";

        public JsonElement InputSchema => JsonHelper.ParseElement(@"
        {
            ""type"": ""object"",
            ""properties"": {}
        }
        ");

        public ToolsCallResult Execute(JsonElement arguments)
        {
            UnityCodeMcpServerSettings settings = UnityCodeMcpServerSettings.Instance;
            string projectPath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, ".."));

            StringBuilder sb = new();
            string messageDirectory = Path.Combine(projectPath, ".unityCodeMcpServer", "messages");
            sb.AppendLine("## Unity Project Info");
            sb.AppendLine();
            sb.AppendLine($"**Project Path:** {projectPath}");
            sb.AppendLine($"**Unity Version:** {Application.unityVersion}");
            sb.AppendLine();
            sb.AppendLine("## UnityCodeMcpServer Settings");
            sb.AppendLine();
            sb.AppendLine($"- **Min Log Level:** {settings.MinLogLevel}");
            sb.AppendLine($"- **Log To File:** {settings.LogToFile}");
            sb.AppendLine();
            sb.AppendLine("### File Server");
            sb.AppendLine($"- **Messages Directory:** {messageDirectory}");
            sb.AppendLine();
            sb.AppendLine("### Script Execution Assemblies");
            sb.AppendLine("**Default Assemblies:**");
            foreach (string assembly in UnityCodeMcpServerSettings.DefaultAssemblyNames)
            {
                sb.AppendLine($"  - {assembly}");
            }

            sb.AppendLine("**Additional Assemblies:**");
            if (settings.AdditionalAssemblyNames == null || settings.AdditionalAssemblyNames.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                foreach (string assembly in settings.AdditionalAssemblyNames)
                {
                    sb.AppendLine($"  - {assembly}");
                }
            }

            return ToolsCallResult.TextResult(sb.ToString());
        }
    }
}
