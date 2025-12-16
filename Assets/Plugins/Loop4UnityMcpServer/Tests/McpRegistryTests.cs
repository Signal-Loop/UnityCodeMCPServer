using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using Cysharp.Threading.Tasks;
using LoopMcpServer.Interfaces;
using LoopMcpServer.Protocol;
using LoopMcpServer.Registry;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace LoopMcpServer.Tests
{
    [TestFixture]
    public class McpRegistryTests
    {
        [Test]
        public void DiscoverAndRegisterAll_FindsTestTools()
        {
            var registry = new McpRegistry();
            registry.DiscoverAndRegisterAll();

            // Should find the sample tools we created
            Assert.That(registry.HasTool("test_sync_tool"), Is.True);
            Assert.That(registry.HasTool("test_async_tool"), Is.True);
        }

        [Test]
        public void DiscoverAndRegisterAll_WithVerbose_FindsAllComponents()
        {
            var registry = new McpRegistry();
            registry.DiscoverAndRegisterAll(true);

            Assert.That(registry.HasTool("test_sync_tool"), Is.True);
            Assert.That(registry.HasTool("test_async_tool"), Is.True);
            Assert.That(registry.HasPrompt("test_prompt"), Is.True);
            Assert.That(registry.HasResource("test://resource"), Is.True);
        }
        [Test]
        public void DiscoverAndRegisterAll_FindsAsyncTestTool()
        {
            var registry = new McpRegistry();
            registry.DiscoverAndRegisterAll();

            Assert.That(registry.HasTool("test_async_tool"), Is.True);
        }

        [Test]
        public void DiscoverAndRegisterAll_FindsTestPrompts()
        {
            var registry = new McpRegistry();
            registry.DiscoverAndRegisterAll();

            // Should find the sample prompt we created
            Assert.That(registry.HasPrompt("test_prompt"), Is.True);
        }

        [Test]
        public void DiscoverAndRegisterAll_FindsTestResources()
        {
            var registry = new McpRegistry();
            registry.DiscoverAndRegisterAll();

            // Should find the sample resources we created
            Assert.That(registry.HasResource("test://resource"), Is.True);
        }

        [Test]
        public void GetToolsList_ReturnsAllTools()
        {
            var registry = new McpRegistry();
            registry.DiscoverAndRegisterAll();

            var result = registry.GetToolsList();

            Assert.That(result.Tools, Is.Not.Empty);
            Assert.That(result.Tools.Exists(t => t.Name == "test_sync_tool"), Is.True);
            Assert.That(result.Tools.Exists(t => t.Name == "test_async_tool"), Is.True);
        }

        [Test]
        public void ExecuteToolAsync_SyncTool_ExecutesSuccessfully()
        {
            var registry = new McpRegistry();
            registry.DiscoverAndRegisterAll();

            var arguments = JsonHelper.ParseElement("{}");
            var result = registry.ExecuteToolAsync("test_sync_tool", arguments).GetAwaiter().GetResult();

            Assert.That(result.IsError, Is.False);
            Assert.That(result.Content, Is.Not.Empty);
            Assert.That(result.Content[0].Text, Does.Contain("Test sync result"));
        }

        [UnityTest]
        public IEnumerator ExecuteToolAsync_AsyncTool_ExecutesSuccessfully() => UniTask.ToCoroutine(async () =>
        {
            var registry = new McpRegistry();
            registry.DiscoverAndRegisterAll();

            var arguments = JsonHelper.ParseElement("{}");
            var result = await registry.ExecuteToolAsync("test_async_tool", arguments);

            Assert.That(result.IsError, Is.False);
            Assert.That(result.Content, Is.Not.Empty);
            Assert.That(result.Content[0].Text, Does.Contain("Test async result"));
        });

        [Test]
        public void ExecuteToolAsync_NonExistentTool_ReturnsError()
        {
            var registry = new McpRegistry();
            registry.DiscoverAndRegisterAll();

            var arguments = JsonHelper.ParseElement("{}");
            var result = registry.ExecuteToolAsync("nonexistent", arguments).GetAwaiter().GetResult();

            Assert.That(result.IsError, Is.True);
            Assert.That(result.Content[0].Text, Does.Contain("not found"));
        }

        [Test]
        public void GetPromptsList_ReturnsAllPrompts()
        {
            var registry = new McpRegistry();
            registry.DiscoverAndRegisterAll();

            var result = registry.GetPromptsList();

            Assert.That(result.Prompts, Is.Not.Empty);
            Assert.That(result.Prompts.Exists(p => p.Name == "test_prompt"), Is.True);
        }

        [Test]
        public void GetPromptMessages_ValidPrompt_ReturnsMessages()
        {
            var registry = new McpRegistry();
            registry.DiscoverAndRegisterAll();

            var arguments = new Dictionary<string, string>();
            var result = registry.GetPromptMessages("test_prompt", arguments);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Messages, Is.Not.Empty);
            Assert.That(result.Messages[0].Content.Text, Does.Contain("Test message"));
        }

        [Test]
        public void GetPromptMessages_NonExistentPrompt_ReturnsNull()
        {
            var registry = new McpRegistry();
            registry.DiscoverAndRegisterAll();

            var result = registry.GetPromptMessages("nonexistent", new Dictionary<string, string>());

            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetResourcesList_ReturnsAllResources()
        {
            var registry = new McpRegistry();
            registry.DiscoverAndRegisterAll();

            var result = registry.GetResourcesList();

            Assert.That(result.Resources, Is.Not.Empty);
            Assert.That(result.Resources.Exists(r => r.Uri == "test://resource"), Is.True);
        }

        [Test]
        public void ReadResource_ValidResource_ReturnsContent()
        {
            var registry = new McpRegistry();
            registry.DiscoverAndRegisterAll();

            var result = registry.ReadResource("test://resource");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Contents, Is.Not.Empty);
            Assert.That(result.Contents[0].Text, Does.Contain("Test resource content"));
        }

        [Test]
        public void ReadResource_NonExistentResource_ReturnsNull()
        {
            var registry = new McpRegistry();
            registry.DiscoverAndRegisterAll();

            var result = registry.ReadResource("nonexistent://resource");

            Assert.That(result, Is.Null);
        }
    }

    #region Test Implementations for Manual Registration Testing

    public class TestSyncTool : ITool
    {
        public string Name => "test_sync_tool";
        public string Description => "A test synchronous tool";
        public JsonElement InputSchema => JsonHelper.ParseElement(@"{""type"": ""object""}");

        public ToolsCallResult Execute(JsonElement arguments)
        {
            return new ToolsCallResult
            {
                IsError = false,
                Content = new List<ContentItem> { ContentItem.TextContent("Test sync result") }
            };
        }
    }

    public class TestAsyncTool : IToolAsync
    {
        public string Name => "test_async_tool";
        public string Description => "A test asynchronous tool";
        public JsonElement InputSchema => JsonHelper.ParseElement(@"{""type"": ""object""}");

        public UniTask<ToolsCallResult> ExecuteAsync(JsonElement arguments)
        {
            return UniTask.FromResult(new ToolsCallResult
            {
                IsError = false,
                Content = new List<ContentItem> { ContentItem.TextContent("Test async result") }
            });
        }
    }

    public class TestPrompt : IPrompt
    {
        public string Name => "test_prompt";
        public string Description => "A test prompt";
        public List<PromptArgument> Arguments => new List<PromptArgument>();

        public PromptsGetResult GetMessages(Dictionary<string, string> arguments)
        {
            return new PromptsGetResult
            {
                Description = "Test prompt result",
                Messages = new List<PromptMessage>
                {
                    new PromptMessage
                    {
                        Role = McpRoles.User,
                        Content = ContentItem.TextContent("Test message")
                    }
                }
            };
        }
    }

    public class TestResource : IResource
    {
        public string Uri => "test://resource";
        public string Name => "Test Resource";
        public string Description => "A test resource";
        public string MimeType => "text/plain";

        public ResourcesReadResult Read()
        {
            return new ResourcesReadResult
            {
                Contents = new List<ResourceContent>
                {
                    new ResourceContent
                    {
                        Uri = Uri,
                        MimeType = MimeType,
                        Text = "Test resource content"
                    }
                }
            };
        }
    }

    #endregion
}
