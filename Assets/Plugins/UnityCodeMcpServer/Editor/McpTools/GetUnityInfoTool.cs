using System.Text;
using System.Text.Json;
using UnityCodeMcpServer.Interfaces;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Settings;
using UnityEditor;
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
- `settings`: The current UnityCodeMcpServerSettings values (startup server mode, ports, timeouts, assemblies, etc.).";

        public JsonElement InputSchema => JsonHelper.ParseElement(@"
        {
            ""type"": ""object"",
            ""properties"": {}
        }
        ");

        public ToolsCallResult Execute(JsonElement arguments)
        {
            var settings = UnityCodeMcpServerSettings.Instance;
            string projectPath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, ".."));

            var sb = new StringBuilder();
            sb.AppendLine("## Unity Project Info");
            sb.AppendLine();
            sb.AppendLine($"**Project Path:** {projectPath}");
            sb.AppendLine($"**Unity Version:** {Application.unityVersion}");
            sb.AppendLine();
            sb.AppendLine("## UnityCodeMcpServer Settings");
            sb.AppendLine();
            sb.AppendLine($"- **Startup Server:** {settings.StartupServer}");
            sb.AppendLine($"- **Min Log Level:** {settings.MinLogLevel}");
            sb.AppendLine();
            sb.AppendLine("### STDIO Server");
            sb.AppendLine($"- **Port:** {settings.StdioPort}");
            sb.AppendLine($"- **Backlog:** {settings.Backlog}");
            sb.AppendLine($"- **Read Timeout (ms):** {settings.ReadTimeoutMs}");
            sb.AppendLine($"- **Write Timeout (ms):** {settings.WriteTimeoutMs}");
            sb.AppendLine();
            sb.AppendLine("### Streamable HTTP Server");
            sb.AppendLine($"- **Port:** {settings.HttpPort}");
            sb.AppendLine($"- **Session Timeout (s):** {settings.SessionTimeoutSeconds}");
            sb.AppendLine($"- **SSE Keep-Alive Interval (s):** {settings.SseKeepAliveIntervalSeconds}");
            sb.AppendLine();
            sb.AppendLine("### Script Execution Assemblies");
            sb.AppendLine("**Default Assemblies:**");
            foreach (var assembly in UnityCodeMcpServerSettings.DefaultAssemblyNames)
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
                foreach (var assembly in settings.AdditionalAssemblyNames)
                {
                    sb.AppendLine($"  - {assembly}");
                }
            }

            return ToolsCallResult.TextResult(sb.ToString());
        }
    }
}
