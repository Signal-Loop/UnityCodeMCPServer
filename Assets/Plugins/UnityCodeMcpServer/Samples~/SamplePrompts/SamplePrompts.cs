using System.Collections.Generic;
using UnityCodeMcpServer.Interfaces;
using UnityCodeMcpServer.Protocol;

namespace UnityCodeMcpServer.SampleTools
{
    /// <summary>
    /// Sample prompt that generates a greeting message
    /// </summary>
    public class GreetingPrompt : IPrompt
    {
        public string Name => "greeting";

        public string Description => "Generates a greeting message for the specified name";

        public List<PromptArgument> Arguments => new List<PromptArgument>
        {
            new PromptArgument
            {
                Name = "name",
                Description = "The name to greet",
                Required = true
            },
            new PromptArgument
            {
                Name = "style",
                Description = "The greeting style (formal, casual, enthusiastic)",
                Required = false
            }
        };

        public PromptsGetResult GetMessages(Dictionary<string, string> arguments)
        {
            var name = arguments.GetValueOrDefault("name", "World");
            var style = arguments.GetValueOrDefault("style", "casual");

            var greeting = style switch
            {
                "formal" => $"Good day, {name}. It is a pleasure to make your acquaintance.",
                "enthusiastic" => $"Hey there, {name}! Great to see you! 🎉",
                _ => $"Hello, {name}!"
            };

            return new PromptsGetResult
            {
                Description = $"Greeting for {name}",
                Messages = new List<PromptMessage>
                {
                    new PromptMessage
                    {
                        Role = McpRoles.User,
                        Content = ContentItem.TextContent(greeting)
                    }
                }
            };
        }
    }
}
