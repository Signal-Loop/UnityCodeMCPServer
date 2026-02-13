using System.Collections.Generic;
using System.Text.Json;
using Cysharp.Threading.Tasks;
using UnityCodeMcpServer.Interfaces;
using UnityCodeMcpServer.Protocol;
using UnityEngine;

namespace UnityCodeMcpServer.SampleTools
{
    /// <summary>
    /// Sample synchronous tool that echoes input text
    /// </summary>
    public class EchoTool : ITool
    {
        public string Name => "echo";

        public string Description => "Echoes the input text back to the caller";

        public JsonElement InputSchema => JsonHelper.ParseElement(@"{
            ""type"": ""object"",
            ""properties"": {
                ""text"": {
                    ""type"": ""string"",
                    ""description"": ""The text to echo""
                }
            },
            ""required"": [""text""]
        }");

        public ToolsCallResult Execute(JsonElement arguments)
        {
            var text = arguments.GetStringOrDefault("text", "");

            return ToolsCallResult.TextResult($"Echo: {text}");
        }
    }

    /// <summary>
    /// Sample asynchronous tool that demonstrates async execution
    /// </summary>
    public class DelayedEchoTool : IToolAsync
    {
        public string Name => "delayed_echo";

        public string Description => "Echoes the input text after a specified delay (demonstrates async tool)";

        public JsonElement InputSchema => JsonHelper.ParseElement(@"{
            ""type"": ""object"",
            ""properties"": {
                ""text"": {
                    ""type"": ""string"",
                    ""description"": ""The text to echo""
                },
                ""delayMs"": {
                    ""type"": ""integer"",
                    ""description"": ""Delay in milliseconds before echoing"",
                    ""default"": 1000
                }
            },
            ""required"": [""text""]
        }");

        public async UniTask<ToolsCallResult> ExecuteAsync(JsonElement arguments)
        {
            var text = arguments.GetStringOrDefault("text", "");
            var delayMs = arguments.GetIntOrDefault("delayMs", 1000);

            await UniTask.Delay(delayMs);

            return ToolsCallResult.TextResult($"Delayed Echo (after {delayMs}ms): {text}");
        }
    }

    /// <summary>
    /// Sample tool that gets Unity Editor information
    /// </summary>
    public class GetUnityInfoTool : ITool
    {
        public string Name => "get_unity_info";

        public string Description => "Gets information about the current Unity Editor environment";

        public JsonElement InputSchema => JsonHelper.ParseElement(@"{
            ""type"": ""object"",
            ""properties"": {},
            ""required"": []
        }");

        public ToolsCallResult Execute(JsonElement arguments)
        {
            var info = new
            {
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                productName = Application.productName,
                companyName = Application.companyName,
                dataPath = Application.dataPath,
                isPlaying = Application.isPlaying,
                isBatchMode = Application.isBatchMode
            };

            return ToolsCallResult.TextResult(JsonHelper.Serialize(info, indented: true));
        }
    }
}
