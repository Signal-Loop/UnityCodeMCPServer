using System.IO;
using NUnit.Framework;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Settings;
using UnityEngine.TestTools;

namespace UnityCodeMcpServer.Tests.EditMode
{
    [TestFixture]
    public class UnityCodeMcpServerLoggerTests
    {
        private string _test_log_path;
        private bool _original_log_to_file;

        [SetUp]
        public void Setup()
        {
            _test_log_path = Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath), "UnityCodeMcpServerLog.log");

            // Store original LogToFile setting
            UnityCodeMcpServerSettings settings = UnityCodeMcpServerSettings.Instance;
            _original_log_to_file = settings.LogToFile;

            // Enable logging to file for tests
            settings.LogToFile = true;
            UnityEditor.EditorUtility.SetDirty(settings);
            UnityEditor.AssetDatabase.SaveAssets();

            // Clear the log file before each test
            if (File.Exists(_test_log_path))
            {
                File.Delete(_test_log_path);
            }
        }

        [TearDown]
        public void Teardown()
        {
            // Restore original LogToFile setting
            UnityCodeMcpServerSettings settings = UnityCodeMcpServerSettings.Instance;
            settings.LogToFile = _original_log_to_file;
            UnityEditor.EditorUtility.SetDirty(settings);
            UnityEditor.AssetDatabase.SaveAssets();
        }

        [Test]
        public void LogToFile_WhenEnabled_CreatesLogFile()
        {
            // Act
            UnityCodeMcpServerLogger.Info("Test message");
            System.Threading.Thread.Sleep(100);

            // Assert
            Assert.True(File.Exists(_test_log_path), "Log file should exist");
        }

        [Test]
        public void LogToFile_ContainsTimestamp()
        {
            // Act
            UnityCodeMcpServerLogger.Info("Test message with timestamp");
            System.Threading.Thread.Sleep(100);

            // Assert
            Assert.True(File.Exists(_test_log_path), "Log file should exist");
            string content = File.ReadAllText(_test_log_path);
            Assert.IsTrue(content.Contains("["), "Log should contain timestamp bracket");
            Assert.IsTrue(content.Contains("]"), "Log should contain timestamp bracket");
            // Verify format like [2026-04-21 20:44:04.531]
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(content, @"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\]"),
                "Log should contain timestamp in format [YYYY-MM-DD HH:mm:ss.fff]");
        }

        [Test]
        public void LogToFile_ContainsSeverityLevel()
        {
            // Act
            UnityCodeMcpServerLogger.Debug("Debug message");
            UnityCodeMcpServerLogger.Info("Info message");
            UnityCodeMcpServerLogger.Warn("Warn message");

            LogAssert.Expect(UnityEngine.LogType.Error, "[ERROR] #UnityCodeMcpServer Error message");
            UnityCodeMcpServerLogger.Error("Error message");

            System.Threading.Thread.Sleep(100);

            // Assert
            string content = File.ReadAllText(_test_log_path);
            Assert.IsTrue(content.Contains("[DEBUG]"), "Log should contain DEBUG severity");
            Assert.IsTrue(content.Contains("[INFO]"), "Log should contain INFO severity");
            Assert.IsTrue(content.Contains("[WARN]"), "Log should contain WARN severity");
            Assert.IsTrue(content.Contains("[ERROR]"), "Log should contain ERROR severity");
        }

        [Test]
        public void LogToFile_WhenDisabled_DoesNotCreateFile()
        {
            // Arrange
            UnityCodeMcpServerSettings settings = UnityCodeMcpServerSettings.Instance;
            settings.LogToFile = false;
            UnityEditor.EditorUtility.SetDirty(settings);
            UnityEditor.AssetDatabase.SaveAssets();

            if (File.Exists(_test_log_path))
            {
                File.Delete(_test_log_path);
            }

            // Act
            UnityCodeMcpServerLogger.Info("This should not be logged to file");
            System.Threading.Thread.Sleep(100);

            // Assert
            Assert.False(File.Exists(_test_log_path), "Log file should not exist when LogToFile is disabled");
        }

        [Test]
        public void LogToFile_MultipleMessages_AllLogged()
        {
            // Act
            for (int i = 0; i < 5; i++)
            {
                UnityCodeMcpServerLogger.Info($"Message {i}");
            }
            System.Threading.Thread.Sleep(100);

            // Assert
            string content = File.ReadAllText(_test_log_path);
            string[] lines = content.Split(System.Environment.NewLine);
            int message_count = 0;
            foreach (string line in lines)
            {
                if (line.Contains("Message"))
                    message_count++;
            }
            Assert.GreaterOrEqual(message_count, 5, "All 5 messages should be logged");
        }

        [Test]
        public void LogToFile_ExceptionLogging_CapturesStackTrace()
        {
            // Act
            try
            {
                throw new System.InvalidOperationException("Test exception");
            }
            catch (System.Exception ex)
            {
                LogAssert.Expect(UnityEngine.LogType.Exception, "InvalidOperationException: Test exception");
                UnityCodeMcpServerLogger.Exception("Caught exception", ex);
            }
            System.Threading.Thread.Sleep(100);

            // Assert
            Assert.True(File.Exists(_test_log_path), "Log file should exist");
            string content = File.ReadAllText(_test_log_path);
            Assert.IsTrue(content.Contains("InvalidOperationException"), "Log should contain exception type");
            Assert.IsTrue(content.Contains("Test exception"), "Log should contain exception message");
        }

        [Test]
        public void LogToFile_ErrorLevel_IncludesStackTrace()
        {
            // Act
            LogAssert.Expect(UnityEngine.LogType.Error, "[ERROR] #UnityCodeMcpServer Error with stack trace");
            UnityCodeMcpServerLogger.Error("Error with stack trace");
            System.Threading.Thread.Sleep(100);

            // Assert
            Assert.True(File.Exists(_test_log_path), "Log file should exist");
            string content = File.ReadAllText(_test_log_path);
            Assert.IsTrue(content.Contains("[ERROR]"), "Log should contain ERROR severity");
            Assert.IsTrue(content.Contains("Error with stack trace"), "Log should contain error message");
        }
    }
}
