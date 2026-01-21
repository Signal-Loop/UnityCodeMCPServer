using System;
using UnityCodeMcpServer.Servers.StreamableHttp;
using NUnit.Framework;

namespace UnityCodeMcpServer.Tests.StreamableHttp
{
    [TestFixture]
    public class ValidationResultTests
    {
        #region Success Tests

        [Test]
        public void Success_ReturnsValidResult()
        {
            var result = ValidationResult.Success();

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.StatusCode, Is.EqualTo(200));
            Assert.That(result.ErrorMessage, Is.Null);
        }

        [Test]
        public void Success_WithSessionId_ReturnsValidResultWithSessionId()
        {
            var result = ValidationResult.Success("test-session-id");

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.SessionId, Is.EqualTo("test-session-id"));
        }

        [Test]
        public void Success_WithoutSessionId_ReturnsNullSessionId()
        {
            var result = ValidationResult.Success();

            Assert.That(result.SessionId, Is.Null);
        }

        #endregion

        #region Failure Tests

        [Test]
        public void Failure_ReturnsInvalidResult()
        {
            var result = ValidationResult.Failure(400, "Bad Request");

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(400));
            Assert.That(result.ErrorMessage, Is.EqualTo("Bad Request"));
        }

        [Test]
        public void Failure_SessionIdIsNull()
        {
            var result = ValidationResult.Failure(403, "Forbidden");

            Assert.That(result.SessionId, Is.Null);
        }

        [Test]
        public void Failure_404NotFound_ReturnsCorrectStatus()
        {
            var result = ValidationResult.Failure(404, "Not Found");

            Assert.That(result.StatusCode, Is.EqualTo(404));
            Assert.That(result.ErrorMessage, Is.EqualTo("Not Found"));
        }

        [Test]
        public void Failure_500ServerError_ReturnsCorrectStatus()
        {
            var result = ValidationResult.Failure(500, "Internal Server Error");

            Assert.That(result.StatusCode, Is.EqualTo(500));
            Assert.That(result.ErrorMessage, Is.EqualTo("Internal Server Error"));
        }

        #endregion

        #region Struct Behavior Tests

        [Test]
        public void ValidationResult_IsValueType()
        {
            var result = ValidationResult.Success();

            Assert.That(result, Is.TypeOf<ValidationResult>());
            Assert.That(typeof(ValidationResult).IsValueType, Is.True);
        }

        [Test]
        public void ValidationResult_DefaultValue_IsInvalid()
        {
            // Default struct should have IsValid = false (default bool)
            var result = default(ValidationResult);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(0));
        }

        #endregion
    }
}
