using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityCodeMcpServer.McpTools;
using UnityCodeMcpServer.Protocol;
using UnityCodeMcpServer.Settings;
using UnityEngine;

namespace UnityCodeMcpServer.Tests.EditMode
{
    [TestFixture]
    public class AssemblyLoadingIntegrationTests
    {
        private UnityCodeMcpServerSettings _testSettings;

        [SetUp]
        public void SetUp()
        {
            _testSettings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_testSettings != null)
            {
                ScriptableObject.DestroyImmediate(_testSettings);
            }
        }

        [Test]
        public void ExecuteCSharpScriptInUnityEditor_UsesDefaultAssemblies()
        {
            ExecuteCSharpScriptInUnityEditor tool = new();
            string scriptJson = JsonSerializer.Serialize(new { script = "return typeof(UnityEngine.GameObject).Assembly.GetName().Name;" });
            JsonElement args = JsonDocument.Parse(scriptJson).RootElement;

            UniTask<ToolsCallResult> task = tool.ExecuteAsync(args);
            task.GetAwaiter().GetResult();
            ToolsCallResult result = task.GetAwaiter().GetResult();

            Assert.That(result.IsError, Is.False);
            Assert.That(result.Content[0].Text, Does.Contain("UnityEngine.CoreModule"));
            Assert.That(result.Content[0].Text, Does.Contain("### Loaded Assemblies"));
            Assert.That(result.Content[0].Text, Does.Contain("UnityEngine.CoreModule, Version="));
            Assert.That(result.Content[0].Text, Does.Contain("UnityEditor.CoreModule, Version="));
        }

        [Test]
        public void ExecuteCSharpScriptInUnityEditor_UsesAdditionalAssemblies()
        {
            // Add an additional assembly to settings
            UnityCodeMcpServerSettings settings = UnityCodeMcpServerSettings.Instance;
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            string additionalAssembly = loadedAssemblies
                .Select(a => a.GetName().Name)
                .FirstOrDefault(name => !UnityCodeMcpServerSettings.DefaultAssemblyNames.Contains(name)
                    && !string.IsNullOrWhiteSpace(name)
                    && name.StartsWith("Unity"));

            if (additionalAssembly != null)
            {
                settings.AddAssembly(additionalAssembly);

                try
                {
                    string[] allAssemblies = settings.GetAllAssemblyNames();
                    Assert.That(allAssemblies, Contains.Item(additionalAssembly));

                    ExecuteCSharpScriptInUnityEditor tool = new();
                    string scriptJson = JsonSerializer.Serialize(new { script = "return \"Assembly loaded successfully\";" });
                    JsonElement args = JsonDocument.Parse(scriptJson).RootElement;

                    UniTask<ToolsCallResult> task = tool.ExecuteAsync(args);
                    task.GetAwaiter().GetResult();
                    ToolsCallResult result = task.GetAwaiter().GetResult();

                    Assert.That(result.IsError, Is.False);
                    Assert.That(result.Content[0].Text, Does.Contain("### Loaded Assemblies"));
                    Assert.That(result.Content[0].Text, Does.Contain(additionalAssembly + ", Version="));
                }
                finally
                {
                    settings.RemoveAssembly(additionalAssembly);
                }
            }
            else
            {
                Assert.Pass("No additional assemblies available for testing");
            }
        }

        [Test]
        public void GetAllAssemblyNames_HandlesMissingAssembliesGracefully()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                settings.AdditionalAssemblyNames.Add("NonExistentAssembly");
                string[] allAssemblies = settings.GetAllAssemblyNames();

                Assert.That(allAssemblies, Contains.Item("NonExistentAssembly"));
                Assert.That(allAssemblies.Length, Is.EqualTo(UnityCodeMcpServerSettings.DefaultAssemblyNames.Length + 1));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void DefaultAssemblyNames_AreAccessibleIfLoaded()
        {
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            string[] loadedNames = loadedAssemblies.Select(a => a.GetName().Name).ToArray();

            // Check that core assemblies that should always be loaded are present
            string[] coreAssemblies = new[]
            {
                "UnityEngine.CoreModule",
                "UnityEditor.CoreModule",
                "System.Core"
            };

            foreach (string assemblyName in coreAssemblies)
            {
                Assert.That(loadedNames, Contains.Item(assemblyName),
                    $"Core assembly '{assemblyName}' should be loaded in the current AppDomain");
            }

            // Count how many default assemblies are currently loaded
            int loadedDefaultCount = UnityCodeMcpServerSettings.DefaultAssemblyNames
                .Count(name => loadedNames.Contains(name));

            // At least half of the default assemblies should be loaded during tests
            Assert.That(loadedDefaultCount, Is.GreaterThan(UnityCodeMcpServerSettings.DefaultAssemblyNames.Length / 2),
                "At least half of the default assemblies should be loaded during tests");
        }

        [Test]
        public void AddAssembly_CanAddFromCurrentAppDomain()
        {
            UnityCodeMcpServerSettings settings = ScriptableObject.CreateInstance<UnityCodeMcpServerSettings>();

            try
            {
                Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                string availableAssembly = loadedAssemblies
                    .Select(a => a.GetName().Name)
                    .FirstOrDefault(name => !UnityCodeMcpServerSettings.DefaultAssemblyNames.Contains(name)
                        && !string.IsNullOrWhiteSpace(name));

                if (availableAssembly != null)
                {
                    bool result = settings.AddAssembly(availableAssembly);

                    Assert.That(result, Is.True);
                    Assert.That(settings.AdditionalAssemblyNames, Contains.Item(availableAssembly));
                }
                else
                {
                    Assert.Pass("No additional assemblies available in current AppDomain");
                }
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }
    }
}
