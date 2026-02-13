using NUnit.Framework;
using System.Text.Json;
using UnityCodeMcpServer.Protocol;

namespace UnityCodeMcpServer.Tests
{
    [TestFixture]
    public class McpMessagesResourceContentTests
    {
        [Test]
        public void ResourceBlobContent_CreatesResourceTypeWithBlob()
        {
            var contentItem = ContentItem.ResourceBlobContent("resource://test-image", "image/png", "SGVsbG8=");

            Assert.That(contentItem.Type, Is.EqualTo(McpContentTypes.Resource));
            Assert.That(contentItem.Resource, Is.Not.Null);
            Assert.That(contentItem.Resource.Uri, Is.EqualTo("resource://test-image"));
            Assert.That(contentItem.Resource.MimeType, Is.EqualTo("image/png"));
            Assert.That(contentItem.Resource.Blob, Is.EqualTo("SGVsbG8="));
            Assert.That(contentItem.Resource.Text, Is.Null);
        }

        [Test]
        public void ResourceBlobContent_SetsBlob_And_OmitsText_InSerializedJson()
        {
            var contentItem = ContentItem.ResourceBlobContent("resource://test-image", "image/png", "SGVsbG8=");

            Assert.That(contentItem.Resource, Is.Not.Null);
            Assert.That(contentItem.Resource.Blob, Is.EqualTo("SGVsbG8="));
            Assert.That(contentItem.Resource.Text, Is.Null);

            var json = JsonHelper.Serialize(contentItem);

            Assert.That(json, Does.Contain("\"resource\""));
            Assert.That(json, Does.Contain("\"blob\":\"SGVsbG8=\""));
            Assert.That(json, Does.Not.Contain("\"text\""));
        }

        [Test]
        public void ResourceBlobContent_Serializes_ToExpectedResourceJsonShape()
        {
            var contentItem = ContentItem.ResourceBlobContent(
                "memory://play_unity_game_video/28f2eae676cb427583741f19fea98b0b.mp4",
                "video/mp4",
                "AAAAGGZ0eXBtcDQyAAAAAG1wNDFpc29tAAAAKHV1");

            var json = JsonHelper.Serialize(contentItem);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.That(root.GetProperty("type").GetString(), Is.EqualTo("resource"));

            var resource = root.GetProperty("resource");
            Assert.That(resource.GetProperty("uri").GetString(), Is.EqualTo("memory://play_unity_game_video/28f2eae676cb427583741f19fea98b0b.mp4"));
            Assert.That(resource.GetProperty("mimeType").GetString(), Is.EqualTo("video/mp4"));
            Assert.That(resource.GetProperty("blob").GetString(), Is.EqualTo("AAAAGGZ0eXBtcDQyAAAAAG1wNDFpc29tAAAAKHV1"));
            Assert.That(resource.TryGetProperty("text", out _), Is.False);
        }
    }
}
