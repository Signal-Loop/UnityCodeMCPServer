using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;

namespace UnityCodeMcpServer.Tests.EditMode
{
    public class PlayUnityGameToolArgumentParsingTests
    {
        private static JsonElement ParseArguments(string json) => JsonSerializer.Deserialize<JsonElement>(json);

        [Test]
        public void TryParseArguments_WithValidDuration_ReturnsTrue()
        {
            JsonElement arguments = ParseArguments(@"{""duration"": 1000}");

            bool result = PlayUnityGameTool.TryParseArguments(arguments, out PlayUnityGameTool.PlayOptions options, out string errorMessage);

            Assert.IsTrue(result);
            Assert.IsNull(errorMessage);
            Assert.AreEqual(1000, options.DurationMs);
            Assert.AreEqual(0, options.Inputs.Count);
        }



        [Test]
        public void TryParseArguments_WithMissingDuration_ReturnsFalse()
        {
            JsonElement arguments = ParseArguments(@"{}");

            bool result = PlayUnityGameTool.TryParseArguments(arguments, out _, out string errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("duration"));
        }

        [Test]
        public void TryParseArguments_WithValidInputs_ParsesCorrectly()
        {
            JsonElement arguments = ParseArguments(@"{
                ""duration"": 500,
                ""input"": [
                    {""action"": ""Player1Up"", ""type"": ""hold""},
                    {""action"": ""Player2Down"", ""type"": ""press""}
                ]
            }");

            bool result = PlayUnityGameTool.TryParseArguments(arguments, out PlayUnityGameTool.PlayOptions options, out string errorMessage);

            Assert.IsTrue(result);
            Assert.IsNull(errorMessage);
            Assert.AreEqual(500, options.DurationMs);
            Assert.AreEqual(2, options.Inputs.Count);
            Assert.AreEqual("Player1Up", options.Inputs[0].ActionName);
            Assert.AreEqual(PlayUnityGameTool.InputType.Hold, options.Inputs[0].Type);
            Assert.AreEqual("Player2Down", options.Inputs[1].ActionName);
            Assert.AreEqual(PlayUnityGameTool.InputType.Press, options.Inputs[1].Type);
        }

        [Test]
        public void TryParseArguments_WithInvalidInputType_ReturnsFalse()
        {
            JsonElement arguments = ParseArguments(@"{
                ""duration"": 500,
                ""input"": [
                    {""action"": ""Player1Up"", ""type"": ""invalid""}
                ]
            }");

            bool result = PlayUnityGameTool.TryParseArguments(arguments, out _, out string errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("press").Or.Contain("hold"));
        }

        [Test]
        public void TryParseArguments_WithEmptyActionName_ReturnsFalse()
        {
            JsonElement arguments = ParseArguments(@"{
                ""duration"": 500,
                ""input"": [
                    {""action"": """", ""type"": ""press""}
                ]
            }");

            bool result = PlayUnityGameTool.TryParseArguments(arguments, out _, out string errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("action name").IgnoreCase);
        }

        [Test]
        public void TryParseArguments_WithBothDurationAndDurationMs_PrefersDuration()
        {
            JsonElement arguments = ParseArguments(@"{""duration"": 1000, ""duration_ms"": 2000}");

            bool result = PlayUnityGameTool.TryParseArguments(arguments, out PlayUnityGameTool.PlayOptions options, out string errorMessage);

            Assert.IsTrue(result);
            Assert.IsNull(errorMessage);
            Assert.AreEqual(1000, options.DurationMs);
        }

        [Test]
        public void TryParseArguments_WithInvalidInputArray_ReturnsFalse()
        {
            JsonElement arguments = ParseArguments(@"{
                ""duration"": 500,
                ""input"": [
                    ""not an object""
                ]
            }");

            bool result = PlayUnityGameTool.TryParseArguments(arguments, out _, out string errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("object"));
        }

        [Test]
        public void PlayOptions_Constructor_InitializesCorrectly()
        {
            List<PlayUnityGameTool.InputRequest> inputs = new()
            {
                new("Test", PlayUnityGameTool.InputType.Press)
            };

            PlayUnityGameTool.PlayOptions options = new(1000, inputs);

            Assert.AreEqual(1000, options.DurationMs);
            Assert.AreEqual(1, options.Inputs.Count);
            Assert.AreEqual("Test", options.Inputs[0].ActionName);
        }

        [Test]
        public void PlayOptions_WithNullInputs_UsesEmptyArray()
        {
            PlayUnityGameTool.PlayOptions options = new(1000, null);

            Assert.AreEqual(1000, options.DurationMs);
            Assert.IsNotNull(options.Inputs);
            Assert.AreEqual(0, options.Inputs.Count);
        }



        [Test]
        public void InputRequest_Constructor_InitializesCorrectly()
        {
            PlayUnityGameTool.InputRequest request = new("TestAction", PlayUnityGameTool.InputType.Hold);

            Assert.AreEqual("TestAction", request.ActionName);
            Assert.AreEqual(PlayUnityGameTool.InputType.Hold, request.Type);
        }



        [Test]
        public void TryParseArguments_WithInputNotArray_ReturnsFalse()
        {
            JsonElement arguments = ParseArguments(@"{""duration"": 500, ""input"": {}}");

            bool result = PlayUnityGameTool.TryParseArguments(arguments, out _, out string errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("input").IgnoreCase);
        }

        [Test]
        public void TryParseArguments_WithDurationNotInteger_ReturnsFalse()
        {
            JsonElement arguments = ParseArguments(@"{""duration"": ""500""}");

            bool result = PlayUnityGameTool.TryParseArguments(arguments, out _, out string errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("duration").IgnoreCase);
            Assert.That(errorMessage, Does.Contain("integer").IgnoreCase);
        }

        [Test]
        public void TryParseArguments_WithInputMissingType_ReturnsFalse()
        {
            JsonElement arguments = ParseArguments(@"{""duration"": 500, ""input"": [{""action"": ""Player1Up""}]}");

            bool result = PlayUnityGameTool.TryParseArguments(arguments, out _, out string errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("type").IgnoreCase);
        }
    }
}
