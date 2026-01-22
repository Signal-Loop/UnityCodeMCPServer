using System.Collections.Generic;
using UnityEditor.TestTools.TestRunner.Api;
using NUnit.Framework.Interfaces;

namespace UnityCodeMcpServer.Tests
{
    public class MockTestResultAdaptor : ITestResultAdaptor
    {
        public ITestAdaptor Test { get; set; }
        public string Name { get; set; } = "MockTest";
        public string FullName { get; set; } = "MockTest";
        public string ResultState { get; set; } = "Passed";
        public UnityEditor.TestTools.TestRunner.Api.TestStatus TestStatus { get; set; } = UnityEditor.TestTools.TestRunner.Api.TestStatus.Passed;
        public double Duration { get; set; } = 1.0;
        public System.DateTime StartTime { get; set; }
        public System.DateTime EndTime { get; set; }
        public string Message { get; set; } = "";
        public string StackTrace { get; set; } = "";
        public int AssertCount { get; set; }
        public int FailCount { get; set; }
        public int PassCount { get; set; }
        public int SkipCount { get; set; }
        public int InconclusiveCount { get; set; }
        public bool HasChildren { get; set; }
        public IEnumerable<ITestResultAdaptor> Children { get; set; } = new List<ITestResultAdaptor>();
        public string Output { get; set; } = "";
        public TNode ToXml() => null;
    }
}
