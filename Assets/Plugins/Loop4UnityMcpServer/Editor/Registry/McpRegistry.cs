using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Cysharp.Threading.Tasks;
using LoopMcpServer.Interfaces;
using LoopMcpServer.Protocol;
using UnityEngine;

namespace LoopMcpServer.Registry
{
    /// <summary>
    /// Registry for MCP tools, prompts, and resources.
    /// Automatically discovers implementations via reflection.
    /// </summary>
    public class McpRegistry
    {
        private readonly Dictionary<string, ITool> _syncTools = new Dictionary<string, ITool>();
        private readonly Dictionary<string, IToolAsync> _asyncTools = new Dictionary<string, IToolAsync>();
        private readonly Dictionary<string, IPrompt> _prompts = new Dictionary<string, IPrompt>();
        private readonly Dictionary<string, IResource> _resources = new Dictionary<string, IResource>();
        private bool _verboseLogging;

        public IReadOnlyDictionary<string, ITool> SyncTools => _syncTools;
        public IReadOnlyDictionary<string, IToolAsync> AsyncTools => _asyncTools;
        public IReadOnlyDictionary<string, IPrompt> Prompts => _prompts;
        public IReadOnlyDictionary<string, IResource> Resources => _resources;

        /// <summary>
        /// Discovers and registers all implementations of ITool, IToolAsync, IPrompt, and IResource
        /// </summary>
        public void DiscoverAndRegisterAll(bool verboseLogging = false)
        {
            _verboseLogging = verboseLogging;
            _syncTools.Clear();
            _asyncTools.Clear();
            _prompts.Clear();
            _resources.Clear();

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                try
                {
                    DiscoverInAssembly(assembly);
                }
                catch (ReflectionTypeLoadException ex)
                {
                    Debug.LogWarning($"{McpProtocol.LogPrefix} Failed to load types from assembly {assembly.FullName}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{McpProtocol.LogPrefix} Error discovering types in assembly {assembly.FullName}: {ex.Message}");
                }
            }

            if (_verboseLogging)
            {
                Debug.Log($"{McpProtocol.LogPrefix} Registry initialized: {_syncTools.Count} sync tools, {_asyncTools.Count} async tools, {_prompts.Count} prompts, {_resources.Count} resources");
            }
        }

        private void DiscoverInAssembly(Assembly assembly)
        {
            var types = assembly.GetTypes();

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface)
                    continue;

                try
                {
                    if (typeof(ITool).IsAssignableFrom(type))
                    {
                        RegisterSyncTool(type);
                    }

                    if (typeof(IToolAsync).IsAssignableFrom(type))
                    {
                        RegisterAsyncTool(type);
                    }

                    if (typeof(IPrompt).IsAssignableFrom(type))
                    {
                        RegisterPrompt(type);
                    }

                    if (typeof(IResource).IsAssignableFrom(type))
                    {
                        RegisterResource(type);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{McpProtocol.LogPrefix} Failed to register type {type.FullName}: {ex.Message}");
                }
            }
        }

        private void RegisterSyncTool(Type type)
        {
            var instance = (ITool)Activator.CreateInstance(type);
            if (_syncTools.ContainsKey(instance.Name))
            {
                Debug.LogWarning($"{McpProtocol.LogPrefix} Duplicate sync tool name: {instance.Name}");
                return;
            }
            _syncTools[instance.Name] = instance;
            if (_verboseLogging)
            {
                Debug.Log($"{McpProtocol.LogPrefix} Registered sync tool: {instance.Name}");
            }
        }

        private void RegisterAsyncTool(Type type)
        {
            var instance = (IToolAsync)Activator.CreateInstance(type);
            if (_asyncTools.ContainsKey(instance.Name))
            {
                Debug.LogWarning($"{McpProtocol.LogPrefix} Duplicate async tool name: {instance.Name}");
                return;
            }
            _asyncTools[instance.Name] = instance;
            if (_verboseLogging)
            {
                Debug.Log($"{McpProtocol.LogPrefix} Registered async tool: {instance.Name}");
            }
        }

        private void RegisterPrompt(Type type)
        {
            var instance = (IPrompt)Activator.CreateInstance(type);
            if (_prompts.ContainsKey(instance.Name))
            {
                Debug.LogWarning($"{McpProtocol.LogPrefix} Duplicate prompt name: {instance.Name}");
                return;
            }
            _prompts[instance.Name] = instance;
            if (_verboseLogging)
            {
                Debug.Log($"{McpProtocol.LogPrefix} Registered prompt: {instance.Name}");
            }
        }

        private void RegisterResource(Type type)
        {
            var instance = (IResource)Activator.CreateInstance(type);
            if (_resources.ContainsKey(instance.Uri))
            {
                Debug.LogWarning($"{McpProtocol.LogPrefix} Duplicate resource URI: {instance.Uri}");
                return;
            }
            _resources[instance.Uri] = instance;
            if (_verboseLogging)
            {
                Debug.Log($"{McpProtocol.LogPrefix} Registered resource: {instance.Uri}");
            }
        }

        /// <summary>
        /// Get all tool definitions for tools/list response
        /// </summary>
        public ToolsListResult GetToolsList()
        {
            var result = new ToolsListResult();

            foreach (var tool in _syncTools.Values)
            {
                result.Tools.Add(new ToolDefinition
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    InputSchema = tool.InputSchema
                });
            }

            foreach (var tool in _asyncTools.Values)
            {
                result.Tools.Add(new ToolDefinition
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    InputSchema = tool.InputSchema
                });
            }

            return result;
        }

        /// <summary>
        /// Execute a tool by name
        /// </summary>
        public async UniTask<ToolsCallResult> ExecuteToolAsync(string name, JsonElement arguments)
        {
            if (_syncTools.TryGetValue(name, out var syncTool))
            {
                try
                {
                    return syncTool.Execute(arguments);
                }
                catch (Exception ex)
                {
                    return new ToolsCallResult
                    {
                        IsError = true,
                        Content = new List<ContentItem> { ContentItem.TextContent($"Tool execution error: {ex.Message}") }
                    };
                }
            }

            if (_asyncTools.TryGetValue(name, out var asyncTool))
            {
                try
                {
                    return await asyncTool.ExecuteAsync(arguments);
                }
                catch (Exception ex)
                {
                    return new ToolsCallResult
                    {
                        IsError = true,
                        Content = new List<ContentItem> { ContentItem.TextContent($"Tool execution error: {ex.Message}") }
                    };
                }
            }

            return new ToolsCallResult
            {
                IsError = true,
                Content = new List<ContentItem> { ContentItem.TextContent($"Tool not found: {name}") }
            };
        }

        /// <summary>
        /// Check if a tool exists
        /// </summary>
        public bool HasTool(string name) => _syncTools.ContainsKey(name) || _asyncTools.ContainsKey(name);

        /// <summary>
        /// Get all prompt definitions for prompts/list response
        /// </summary>
        public PromptsListResult GetPromptsList()
        {
            var result = new PromptsListResult();

            foreach (var prompt in _prompts.Values)
            {
                result.Prompts.Add(new PromptDefinition
                {
                    Name = prompt.Name,
                    Description = prompt.Description,
                    Arguments = prompt.Arguments
                });
            }

            return result;
        }

        /// <summary>
        /// Get prompt messages by name
        /// </summary>
        public PromptsGetResult GetPromptMessages(string name, Dictionary<string, string> arguments)
        {
            if (_prompts.TryGetValue(name, out var prompt))
            {
                return prompt.GetMessages(arguments);
            }
            return null;
        }

        /// <summary>
        /// Check if a prompt exists
        /// </summary>
        public bool HasPrompt(string name) => _prompts.ContainsKey(name);

        /// <summary>
        /// Get all resource definitions for resources/list response
        /// </summary>
        public ResourcesListResult GetResourcesList()
        {
            var result = new ResourcesListResult();

            foreach (var resource in _resources.Values)
            {
                result.Resources.Add(new ResourceDefinition
                {
                    Uri = resource.Uri,
                    Name = resource.Name,
                    Description = resource.Description,
                    MimeType = resource.MimeType
                });
            }

            return result;
        }

        /// <summary>
        /// Read a resource by URI
        /// </summary>
        public ResourcesReadResult ReadResource(string uri)
        {
            if (_resources.TryGetValue(uri, out var resource))
            {
                return resource.Read();
            }
            return null;
        }

        /// <summary>
        /// Check if a resource exists
        /// </summary>
        public bool HasResource(string uri) => _resources.ContainsKey(uri);
    }
}
