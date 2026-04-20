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

        [Test]
        public void IsExpectedClientDisconnect_ReturnsTrueForConnectionResetWrappedInIOException()
        {
            var method = typeof(UnityCodeMcpTcpServer).GetMethod(
                "IsExpectedClientDisconnect",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);

            var socketException = new SocketException((int)SocketError.ConnectionReset);
            var ioException = new IOException("connection reset", socketException);
            var result = (bool)method.Invoke(null, new object[] { ioException });

            Assert.That(result, Is.True);
        }

        [Test]
        public void ActiveClientCount_ExposedAsPublicProperty()
        {
            // ActiveClientCount should be accessible for diagnostics logging
            var property = typeof(UnityCodeMcpTcpServer).GetProperty(
                "ActiveClientCount",
                BindingFlags.Public | BindingFlags.Static);

            Assert.That(property, Is.Not.Null, "ActiveClientCount property should exist");
            Assert.That(property.PropertyType, Is.EqualTo(typeof(int)));

            var value = (int)property.GetValue(null);
            Assert.That(value, Is.GreaterThanOrEqualTo(0), "ActiveClientCount should never be negative");
        }

        [Test]
        public void StopServer_AcceptsShutdownReasonParameter()
        {
            // StopServer(string) should exist for diagnostic logging
            var method = typeof(UnityCodeMcpTcpServer).GetMethod(
                "StopServer",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);

            Assert.That(method, Is.Not.Null, "StopServer(string reason) overload should exist");
        }
    }
}