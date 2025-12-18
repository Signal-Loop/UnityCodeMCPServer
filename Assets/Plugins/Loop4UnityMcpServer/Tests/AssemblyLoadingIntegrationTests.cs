using System;
using System.Linq;
using System.Text.Json;
using Cysharp.Threading.Tasks;
using LoopMcpServer.Settings;
using LoopMcpServer.Tools;
using NUnit.Framework;
using UnityEngine;

namespace LoopMcpServer.Tests
{
    [TestFixture]
    public class AssemblyLoadingIntegrationTests
    {
        private LoopMcpServerSettings _testSettings;

        [SetUp]
        public void SetUp()
        {
            _testSettings = ScriptableObject.CreateInstance<LoopMcpServerSettings>();
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
        public void ScriptExecutionTool_UsesDefaultAssemblies()
        {
            var tool = new ScriptExecutionTool();
            var scriptJson = JsonSerializer.Serialize(new { script = "return typeof(UnityEngine.GameObject).Assembly.GetName().Name;" });
            var args = JsonDocument.Parse(scriptJson).RootElement;

            var task = tool.ExecuteAsync(args);
            task.GetAwaiter().GetResult();
            var result = task.GetAwaiter().GetResult();

            Assert.That(result.IsError, Is.False);
            Assert.That(result.Content[0].Text, Does.Contain("UnityEngine.CoreModule"));
            Assert.That(result.Content[0].Text, Does.Contain("### Loaded Assemblies"));
            Assert.That(result.Content[0].Text, Does.Contain("UnityEngine.CoreModule.dll"));
            Assert.That(result.Content[0].Text, Does.Contain("UnityEditor.CoreModule.dll"));
        }

        [Test]
        public void ScriptExecutionTool_UsesAdditionalAssemblies()
        {
            // Add an additional assembly to settings
            var settings = LoopMcpServerSettings.Instance;
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            var additionalAssembly = loadedAssemblies
                .Select(a => a.GetName().Name)
                .FirstOrDefault(name => !LoopMcpServerSettings.DefaultAssemblyNames.Contains(name)
                    && !string.IsNullOrWhiteSpace(name)
                    && name.StartsWith("Unity"));

            if (additionalAssembly != null)
            {
                settings.AddAssembly(additionalAssembly);

                try
                {
                    var allAssemblies = settings.GetAllAssemblyNames();
                    Assert.That(allAssemblies, Contains.Item(additionalAssembly));

                    var tool = new ScriptExecutionTool();
                    var scriptJson = JsonSerializer.Serialize(new { script = "return \"Assembly loaded successfully\";" });
                    var args = JsonDocument.Parse(scriptJson).RootElement;

                    var task = tool.ExecuteAsync(args);
                    task.GetAwaiter().GetResult();
                    var result = task.GetAwaiter().GetResult();

                    Assert.That(result.IsError, Is.False);
                    Assert.That(result.Content[0].Text, Does.Contain("### Loaded Assemblies"));
                    Assert.That(result.Content[0].Text, Does.Contain(additionalAssembly + ".dll"));
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
            var settings = ScriptableObject.CreateInstance<LoopMcpServerSettings>();

            try
            {
                settings.AdditionalAssemblyNames.Add("NonExistentAssembly");
                var allAssemblies = settings.GetAllAssemblyNames();

                Assert.That(allAssemblies, Contains.Item("NonExistentAssembly"));
                Assert.That(allAssemblies.Length, Is.EqualTo(LoopMcpServerSettings.DefaultAssemblyNames.Length + 1));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(settings);
            }
        }

        [Test]
        public void DefaultAssemblyNames_AreAccessibleIfLoaded()
        {
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            var loadedNames = loadedAssemblies.Select(a => a.GetName().Name).ToArray();

            // Check that core assemblies that should always be loaded are present
            var coreAssemblies = new[]
            {
                "UnityEngine.CoreModule",
                "UnityEditor.CoreModule",
                "System.Core"
            };

            foreach (var assemblyName in coreAssemblies)
            {
                Assert.That(loadedNames, Contains.Item(assemblyName),
                    $"Core assembly '{assemblyName}' should be loaded in the current AppDomain");
            }

            // Count how many default assemblies are currently loaded
            var loadedDefaultCount = LoopMcpServerSettings.DefaultAssemblyNames
                .Count(name => loadedNames.Contains(name));

            // At least half of the default assemblies should be loaded during tests
            Assert.That(loadedDefaultCount, Is.GreaterThan(LoopMcpServerSettings.DefaultAssemblyNames.Length / 2),
                "At least half of the default assemblies should be loaded during tests");
        }

        [Test]
        public void AddAssembly_CanAddFromCurrentAppDomain()
        {
            var settings = ScriptableObject.CreateInstance<LoopMcpServerSettings>();

            try
            {
                var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                var availableAssembly = loadedAssemblies
                    .Select(a => a.GetName().Name)
                    .FirstOrDefault(name => !LoopMcpServerSettings.DefaultAssemblyNames.Contains(name)
                        && !string.IsNullOrWhiteSpace(name));

                if (availableAssembly != null)
                {
                    var result = settings.AddAssembly(availableAssembly);

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
