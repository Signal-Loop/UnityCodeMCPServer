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
            MethodInfo method = typeof(UnityCodeMcpTcpServer).GetMethod(
                "IsExpectedClientDisconnect",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);

            SocketException socketException = new((int)SocketError.ConnectionAborted);
            IOException ioException = new("transport aborted", socketException);
            bool result = (bool)method.Invoke(null, new object[] { ioException });

            Assert.That(result, Is.True);
        }

        [Test]
        public void IsExpectedClientDisconnect_ReturnsFalseForUnexpectedExceptions()
        {
            MethodInfo method = typeof(UnityCodeMcpTcpServer).GetMethod(
                "IsExpectedClientDisconnect",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);

            bool result = (bool)method.Invoke(null, new object[] { new InvalidOperationException("boom") });

            Assert.That(result, Is.False);
        }

        [Test]
        public void IsExpectedClientDisconnect_ReturnsTrueForConnectionResetWrappedInIOException()
        {
            MethodInfo method = typeof(UnityCodeMcpTcpServer).GetMethod(
                "IsExpectedClientDisconnect",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);

            SocketException socketException = new((int)SocketError.ConnectionReset);
            IOException ioException = new("connection reset", socketException);
            bool result = (bool)method.Invoke(null, new object[] { ioException });

            Assert.That(result, Is.True);
        }

        [Test]
        public void ActiveClientCount_ExposedAsPublicProperty()
        {
            // ActiveClientCount should be accessible for diagnostics logging
            PropertyInfo property = typeof(UnityCodeMcpTcpServer).GetProperty(
                "ActiveClientCount",
                BindingFlags.Public | BindingFlags.Static);

            Assert.That(property, Is.Not.Null, "ActiveClientCount property should exist");
            Assert.That(property.PropertyType, Is.EqualTo(typeof(int)));

            int value = (int)property.GetValue(null);
            Assert.That(value, Is.GreaterThanOrEqualTo(0), "ActiveClientCount should never be negative");
        }

        [Test]
        public void StopServer_AcceptsShutdownReasonParameter()
        {
            // StopServer(string) should exist for diagnostic logging
            MethodInfo method = typeof(UnityCodeMcpTcpServer).GetMethod(
                "StopServer",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);

            Assert.That(method, Is.Not.Null, "StopServer(string reason) overload should exist");
        }
    }
}
