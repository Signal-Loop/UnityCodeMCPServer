using System.Collections.Generic;
using NUnit.Framework;
using UnityCodeMcpServer.Helpers;
using UnityEngine;

namespace UnityCodeMcpServer.Tests.EditMode
{
    public class UnityConsoleLogReaderTests
    {
        [Test]
        public void SelectTail_ReturnsAllEntries_WhenLimitExceedsCount()
        {
            IReadOnlyList<UnityConsoleLogEntry> tail = UnityConsoleLogReader.SelectTail(
                new[]
                {
                    new UnityConsoleLogEntry("first"),
                    new UnityConsoleLogEntry("second")
                },
                10);

            Assert.AreEqual(2, tail.Count);
            Assert.AreEqual("first", tail[0].Message);
            Assert.AreEqual("second", tail[1].Message);
        }

        [Test]
        public void SelectTail_ReturnsNewestEntries_InOriginalOrder()
        {
            IReadOnlyList<UnityConsoleLogEntry> tail = UnityConsoleLogReader.SelectTail(
                new[]
                {
                    new UnityConsoleLogEntry("one"),
                    new UnityConsoleLogEntry("two"),
                    new UnityConsoleLogEntry("three"),
                    new UnityConsoleLogEntry("four")
                },
                2);

            Assert.AreEqual(2, tail.Count);
            Assert.AreEqual("three", tail[0].Message);
            Assert.AreEqual("four", tail[1].Message);
        }

        [Test]
        public void SelectTail_PreservesStructuredFields()
        {
            IReadOnlyList<UnityConsoleLogEntry> tail = UnityConsoleLogReader.SelectTail(
                new[]
                {
                    new UnityConsoleLogEntry("old", "old-stack", UnityConsoleLogSeverity.Info),
                    new UnityConsoleLogEntry("new", "new-stack", UnityConsoleLogSeverity.Warning)
                },
                2);

            Assert.AreEqual(2, tail.Count);
            Assert.AreEqual("new", tail[1].Message);
            Assert.AreEqual("new-stack", tail[1].StackTrace);
            Assert.AreEqual(UnityConsoleLogSeverity.Warning, tail[1].Severity);
        }

        [Test]
        public void ReadTail_PreservesStackTrace_ForInfoEntries()
        {
            string probeId = "reader-info-" + System.Guid.NewGuid().ToString("N");
            Debug.Log(probeId);

            UnityConsoleLogReadResult result = UnityConsoleLogReader.ReadTail(20);
            UnityConsoleLogEntry? matchingEntry = null;
            for (int i = result.Entries.Count - 1; i >= 0; i--)
            {
                if (result.Entries[i].Message.Contains(probeId))
                {
                    matchingEntry = result.Entries[i];
                    break;
                }
            }

            Assert.IsTrue(matchingEntry.HasValue, "Expected the reader to return the probe log entry.");
            Assert.AreEqual(UnityConsoleLogSeverity.Info, matchingEntry.Value.Severity);
            Assert.IsNotNull(matchingEntry.Value.StackTrace);
            StringAssert.Contains("UnityEngine.Debug:Log", matchingEntry.Value.StackTrace);
        }
    }
}
