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
            var arguments = ParseArguments(@"{""duration"": 1000}");

            var result = PlayUnityGameTool.TryParseArguments(arguments, out var options, out var errorMessage);

            Assert.IsTrue(result);
            Assert.IsNull(errorMessage);
            Assert.AreEqual(1000, options.DurationMs);
            Assert.AreEqual(0, options.Inputs.Count);
            Assert.AreEqual(640, options.MaxHeight);
            Assert.AreEqual(50_000_000, options.MaxBase64Bytes);
        }

        [Test]
        public void TryParseArguments_WithMaxHeight_ParsesCorrectly()
        {
            var arguments = ParseArguments(@"{""duration"": 1000, ""max_height"": 321}");

            var result = PlayUnityGameTool.TryParseArguments(arguments, out var options, out var errorMessage);

            Assert.IsTrue(result);
            Assert.IsNull(errorMessage);
            Assert.AreEqual(321, options.MaxHeight);
        }

        [Test]
        public void TryParseArguments_WithInvalidMaxHeight_ReturnsFalse()
        {
            var arguments = ParseArguments(@"{""duration"": 1000, ""max_height"": 0}");

            var result = PlayUnityGameTool.TryParseArguments(arguments, out _, out var errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("max_height").IgnoreCase);
        }

        [Test]
        public void TryParseArguments_WithMaxBase64Bytes_ParsesCorrectly()
        {
            var arguments = ParseArguments(@"{""duration"": 1000, ""max_base64_bytes"": 12345}");

            var result = PlayUnityGameTool.TryParseArguments(arguments, out var options, out var errorMessage);

            Assert.IsTrue(result);
            Assert.IsNull(errorMessage);
            Assert.AreEqual(12345, options.MaxBase64Bytes);
        }

        [Test]
        public void TryParseArguments_WithInvalidMaxBase64Bytes_ReturnsFalse()
        {
            var arguments = ParseArguments(@"{""duration"": 1000, ""max_base64_bytes"": 0}");

            var result = PlayUnityGameTool.TryParseArguments(arguments, out _, out var errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("max_base64_bytes").IgnoreCase);
        }

        [Test]
        public void TryParseArguments_WithMissingDuration_ReturnsFalse()
        {
            var arguments = ParseArguments(@"{}");

            var result = PlayUnityGameTool.TryParseArguments(arguments, out _, out var errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("duration"));
        }

        [Test]
        public void TryParseArguments_WithValidInputs_ParsesCorrectly()
        {
            var arguments = ParseArguments(@"{
                ""duration"": 500,
                ""input"": [
                    {""action"": ""Player1Up"", ""type"": ""hold""},
                    {""action"": ""Player2Down"", ""type"": ""press""}
                ]
            }");

            var result = PlayUnityGameTool.TryParseArguments(arguments, out var options, out var errorMessage);

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
            var arguments = ParseArguments(@"{
                ""duration"": 500,
                ""input"": [
                    {""action"": ""Player1Up"", ""type"": ""invalid""}
                ]
            }");

            var result = PlayUnityGameTool.TryParseArguments(arguments, out _, out var errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("press").Or.Contain("hold"));
        }

        [Test]
        public void TryParseArguments_WithEmptyActionName_ReturnsFalse()
        {
            var arguments = ParseArguments(@"{
                ""duration"": 500,
                ""input"": [
                    {""action"": """", ""type"": ""press""}
                ]
            }");

            var result = PlayUnityGameTool.TryParseArguments(arguments, out _, out var errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("action name").IgnoreCase);
        }

        [Test]
        public void TryParseArguments_WithBothDurationAndDurationMs_PrefersDuration()
        {
            var arguments = ParseArguments(@"{""duration"": 1000, ""duration_ms"": 2000}");

            var result = PlayUnityGameTool.TryParseArguments(arguments, out var options, out var errorMessage);

            Assert.IsTrue(result);
            Assert.IsNull(errorMessage);
            Assert.AreEqual(1000, options.DurationMs);
        }

        [Test]
        public void TryParseArguments_WithInvalidInputArray_ReturnsFalse()
        {
            var arguments = ParseArguments(@"{
                ""duration"": 500,
                ""input"": [
                    ""not an object""
                ]
            }");

            var result = PlayUnityGameTool.TryParseArguments(arguments, out _, out var errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("object"));
        }

        [Test]
        public void PlayOptions_Constructor_InitializesCorrectly()
        {
            var inputs = new List<PlayUnityGameTool.InputRequest>
            {
                new PlayUnityGameTool.InputRequest("Test", PlayUnityGameTool.InputType.Press)
            };

            var options = new PlayUnityGameTool.PlayOptions(1000, inputs);

            Assert.AreEqual(1000, options.DurationMs);
            Assert.AreEqual(1, options.Inputs.Count);
            Assert.AreEqual("Test", options.Inputs[0].ActionName);
            Assert.AreEqual(640, options.MaxHeight);
            Assert.AreEqual(50_000_000, options.MaxBase64Bytes);
        }

        [Test]
        public void PlayOptions_WithNullInputs_UsesEmptyArray()
        {
            var options = new PlayUnityGameTool.PlayOptions(1000, null);

            Assert.AreEqual(1000, options.DurationMs);
            Assert.IsNotNull(options.Inputs);
            Assert.AreEqual(0, options.Inputs.Count);
        }

        [TestCase(1920, 1080, 640, 1137, 640)]
        [TestCase(1280, 720, 640, 1137, 640)]
        [TestCase(640, 480, 640, 640, 480)]
        [TestCase(1024, 2048, 640, 320, 640)]
        public void GetScaledDimensionsToMaxHeight_ReturnsExpectedValues(int width, int height, int maxHeight, int expectedWidth, int expectedHeight)
        {
            PlayUnityGameTool.GetScaledDimensionsToMaxHeight(width, height, maxHeight, out var resultWidth, out var resultHeight);

            Assert.AreEqual(expectedWidth, resultWidth);
            Assert.AreEqual(expectedHeight, resultHeight);
        }

        [Test]
        public void GetScaledDimensionsToMaxHeight_WithInvalidDimensions_ReturnsOneByOne()
        {
            PlayUnityGameTool.GetScaledDimensionsToMaxHeight(0, 0, 640, out var resultWidth, out var resultHeight);

            Assert.AreEqual(1, resultWidth);
            Assert.AreEqual(1, resultHeight);
        }

        [Test]
        public void InputRequest_Constructor_InitializesCorrectly()
        {
            var request = new PlayUnityGameTool.InputRequest("TestAction", PlayUnityGameTool.InputType.Hold);

            Assert.AreEqual("TestAction", request.ActionName);
            Assert.AreEqual(PlayUnityGameTool.InputType.Hold, request.Type);
        }

        [Test]
        public void CaptureResult_Success_CreatesCorrectResult()
        {
            var result = PlayUnityGameTool.CaptureResult.Success("base64data", "image/png");

            Assert.IsFalse(result.IsError);
            Assert.AreEqual("base64data", result.Base64Data);
            Assert.AreEqual("image/png", result.MimeType);
            Assert.IsNull(result.ErrorMessage);
        }

        [Test]
        public void CaptureResult_Error_CreatesCorrectResult()
        {
            var result = PlayUnityGameTool.CaptureResult.Error("Test error");

            Assert.IsTrue(result.IsError);
            Assert.IsNull(result.Base64Data);
            Assert.IsNull(result.MimeType);
            Assert.AreEqual("Test error", result.ErrorMessage);
        }

        [Test]
        public void TryParseArguments_WithInputNotArray_ReturnsFalse()
        {
            var arguments = ParseArguments(@"{""duration"": 500, ""input"": {}}");

            var result = PlayUnityGameTool.TryParseArguments(arguments, out _, out var errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("input").IgnoreCase);
        }

        [Test]
        public void TryParseArguments_WithDurationNotInteger_ReturnsFalse()
        {
            var arguments = ParseArguments(@"{""duration"": ""500""}");

            var result = PlayUnityGameTool.TryParseArguments(arguments, out _, out var errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("duration").IgnoreCase);
            Assert.That(errorMessage, Does.Contain("integer").IgnoreCase);
        }

        [Test]
        public void TryParseArguments_WithInputMissingType_ReturnsFalse()
        {
            var arguments = ParseArguments(@"{""duration"": 500, ""input"": [{""action"": ""Player1Up""}]}");

            var result = PlayUnityGameTool.TryParseArguments(arguments, out _, out var errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("type").IgnoreCase);
        }
    }
}