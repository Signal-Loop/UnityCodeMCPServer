using System.Text.Json;
using NUnit.Framework;

namespace UnityCodeMcpServer.Tests.EditMode
{
    public class GetUnityGameViewWindowScreenshotToolTests
    {
        private static JsonElement ParseArguments(string json) => JsonSerializer.Deserialize<JsonElement>(json);

        [Test]
        public void TryParseMaxHeight_WithoutMaxHeight_ReturnsDefaultValue()
        {
            JsonElement arguments = ParseArguments(@"{}");

            bool result = GetUnityGameViewWindowScreenshotTool.TryParseMaxHeight(arguments, out int maxHeight, out string errorMessage);

            Assert.IsTrue(result);
            Assert.IsNull(errorMessage);
            Assert.AreEqual(640, maxHeight);
        }

        [Test]
        public void TryParseMaxHeight_WithValidMaxHeight_ParsesCorrectly()
        {
            JsonElement arguments = ParseArguments(@"{""max_height"": 321}");

            bool result = GetUnityGameViewWindowScreenshotTool.TryParseMaxHeight(arguments, out int maxHeight, out string errorMessage);

            Assert.IsTrue(result);
            Assert.IsNull(errorMessage);
            Assert.AreEqual(321, maxHeight);
        }

        [Test]
        public void TryParseMaxHeight_WithInvalidMaxHeight_ReturnsFalse()
        {
            JsonElement arguments = ParseArguments(@"{""max_height"": 0}");

            bool result = GetUnityGameViewWindowScreenshotTool.TryParseMaxHeight(arguments, out _, out string errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("greater than 0").IgnoreCase);
        }

        [Test]
        public void TryParseMaxHeight_WithNegativeMaxHeight_ReturnsFalse()
        {
            JsonElement arguments = ParseArguments(@"{""max_height"": -100}");

            bool result = GetUnityGameViewWindowScreenshotTool.TryParseMaxHeight(arguments, out _, out string errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("greater than 0").IgnoreCase);
        }

        [Test]
        public void TryParseMaxHeight_WithNonIntegerValue_ReturnsFalse()
        {
            JsonElement arguments = ParseArguments(@"{""max_height"": ""not a number""}");

            bool result = GetUnityGameViewWindowScreenshotTool.TryParseMaxHeight(arguments, out _, out string errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("integer").IgnoreCase);
        }

        [TestCase(1920, 1080, 640, 1137, 640)]
        [TestCase(1280, 720, 640, 1137, 640)]
        [TestCase(640, 480, 640, 640, 480)]
        [TestCase(1024, 2048, 640, 320, 640)]
        [TestCase(800, 600, 800, 800, 600)]
        public void GetScaledDimensionsToMaxHeight_ReturnsExpectedValues(int width, int height, int maxHeight, int expectedWidth, int expectedHeight)
        {
            GetUnityGameViewWindowScreenshotTool.GetScaledDimensionsToMaxHeight(width, height, maxHeight, out int resultWidth, out int resultHeight);

            Assert.AreEqual(expectedWidth, resultWidth);
            Assert.AreEqual(expectedHeight, resultHeight);
        }

        [Test]
        public void GetScaledDimensionsToMaxHeight_WithHeightEqualToMaxHeight_NoScalingNeeded()
        {
            GetUnityGameViewWindowScreenshotTool.GetScaledDimensionsToMaxHeight(1920, 640, 640, out int resultWidth, out int resultHeight);

            Assert.AreEqual(1920, resultWidth);
            Assert.AreEqual(640, resultHeight);
        }

        [Test]
        public void GetScaledDimensionsToMaxHeight_WithHeightLessThanMaxHeight_NoScalingNeeded()
        {
            GetUnityGameViewWindowScreenshotTool.GetScaledDimensionsToMaxHeight(1920, 600, 640, out int resultWidth, out int resultHeight);

            Assert.AreEqual(1920, resultWidth);
            Assert.AreEqual(600, resultHeight);
        }

        [Test]
        public void GetScaledDimensionsToMaxHeight_WithInvalidDimensions_ReturnsOneByOne()
        {
            GetUnityGameViewWindowScreenshotTool.GetScaledDimensionsToMaxHeight(0, 0, 640, out int resultWidth, out int resultHeight);

            Assert.AreEqual(1, resultWidth);
            Assert.AreEqual(1, resultHeight);
        }

        [Test]
        public void GetScaledDimensionsToMaxHeight_WithZeroWidth_ReturnsOneByOne()
        {
            GetUnityGameViewWindowScreenshotTool.GetScaledDimensionsToMaxHeight(0, 480, 640, out int resultWidth, out int resultHeight);

            Assert.AreEqual(1, resultWidth);
            Assert.AreEqual(1, resultHeight);
        }

        [Test]
        public void GetScaledDimensionsToMaxHeight_WithNegativeHeight_ReturnsOneByOne()
        {
            GetUnityGameViewWindowScreenshotTool.GetScaledDimensionsToMaxHeight(1920, -1080, 640, out int resultWidth, out int resultHeight);

            Assert.AreEqual(1, resultWidth);
            Assert.AreEqual(1, resultHeight);
        }

        [Test]
        public void GetScaledDimensionsToMaxHeight_MaintainsAspectRatio()
        {
            // 16:9 aspect ratio at 1920x1080
            GetUnityGameViewWindowScreenshotTool.GetScaledDimensionsToMaxHeight(1920, 1080, 540, out int resultWidth, out int resultHeight);

            // Expected: 960x540 (maintains 16:9 ratio)
            Assert.AreEqual(960, resultWidth);
            Assert.AreEqual(540, resultHeight);

            // Verify aspect ratio is maintained
            double originalRatio = 1920.0 / 1080.0;
            double scaledRatio = resultWidth / (double)resultHeight;
            Assert.That(scaledRatio, Is.EqualTo(originalRatio).Within(0.01));
        }
    }
}
