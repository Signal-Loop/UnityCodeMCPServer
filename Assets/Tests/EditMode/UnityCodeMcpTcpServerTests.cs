using System;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using UnityCodeMcpServer.Servers.Tcp;

namespace UnityCodeMcpServer.Tests.EditMode
{
    [TestFixture]
    public class UnityCodeMcpTcpServerTests
    {
        [Test]
        public void IsExpectedClientDisconnect_ReturnsTrueForConnectionAbortWrappedInIOException()
        {
            var method = typeof(UnityCodeMcpTcpServer).GetMethod(
                "IsExpectedClientDisconnect",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);

            var socketException = new SocketException((int)SocketError.ConnectionAborted);
            var ioException = new IOException("transport aborted", socketException);
            var result = (bool)method.Invoke(null, new object[] { ioException });

            Assert.That(result, Is.True);
        }

        [Test]
        public void IsExpectedClientDisconnect_ReturnsFalseForUnexpectedExceptions()
        {
            var method = typeof(UnityCodeMcpTcpServer).GetMethod(
                "IsExpectedClientDisconnect",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);

            var result = (bool)method.Invoke(null, new object[] { new InvalidOperationException("boom") });

            Assert.That(result, Is.False);
        }
    }
}