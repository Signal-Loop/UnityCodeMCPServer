using NUnit.Framework;
using UnityCodeMcpServer.Helpers;

namespace UnityCodeMcpServer.Tests.EditMode
{
    public class UnityConsoleLogReaderTests
    {
        [Test]
        public void SelectTail_ReturnsAllEntries_WhenLimitExceedsCount()
        {
            var tail = UnityConsoleLogReader.SelectTail(
                new[]
                {
                    new UnityConsoleLogEntry("first", null),
                    new UnityConsoleLogEntry("second", null)
                },
                10);

            Assert.AreEqual(2, tail.Count);
            Assert.AreEqual("first", tail[0].Message);
            Assert.AreEqual("second", tail[1].Message);
        }

        [Test]
        public void SelectTail_ReturnsNewestEntries_InOriginalOrder()
        {
            var tail = UnityConsoleLogReader.SelectTail(
                new[]
                {
                    new UnityConsoleLogEntry("one", null),
                    new UnityConsoleLogEntry("two", null),
                    new UnityConsoleLogEntry("three", null),
                    new UnityConsoleLogEntry("four", null)
                },
                2);

            Assert.AreEqual(2, tail.Count);
            Assert.AreEqual("three", tail[0].Message);
            Assert.AreEqual("four", tail[1].Message);
        }

        [Test]
        public void FormatEntries_AddsTailHeader_WhenEntriesWereTruncated()
        {
            string text = UnityConsoleLogReader.FormatEntries(
                new[]
                {
                    new UnityConsoleLogEntry("three", null),
                    new UnityConsoleLogEntry("four", null)
                },
                4,
                2);

            StringAssert.Contains("Showing last 2 logs (Total: 4)", text);
            StringAssert.Contains("three", text);
            StringAssert.Contains("four", text);
        }

        [Test]
        public void FormatEntries_ReturnsPlaceholder_WhenEmpty()
        {
            string text = UnityConsoleLogReader.FormatEntries(System.Array.Empty<UnityConsoleLogEntry>(), 0, 1);

            Assert.AreEqual("(No console logs available)", text);
        }
    }
}