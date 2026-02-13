using NUnit.Framework;
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
    }
}
