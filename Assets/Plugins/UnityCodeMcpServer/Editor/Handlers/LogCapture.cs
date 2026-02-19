
using System;
using System.Text;
using UnityEngine;

namespace UnityCodeMcpServer.Handlers
{
    public class LogCapture : IDisposable
    {
        private readonly StringBuilder _logBuilder = new StringBuilder();
        private readonly StringBuilder _errorBuilder = new StringBuilder();
        private bool _isRunning;

        public string Logs => CombineLogs();

        public string ErrorLog => _errorBuilder.ToString();

        public bool HasErrors => _errorBuilder.Length > 0;

        public void Start()
        {
            if (_isRunning)
            {
                return;
            }

            Application.logMessageReceived += HandleLog;
            _isRunning = true;
        }

        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            Application.logMessageReceived -= HandleLog;
            _isRunning = false;
        }

        public void Dispose()
        {
            Stop();
        }

        private void HandleLog(string condition, string stackTrace, LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    _errorBuilder.AppendLine(condition);
                    if (!string.IsNullOrWhiteSpace(stackTrace))
                    {
                        _errorBuilder.AppendLine(stackTrace);
                    }
                    break;
                default:
                    _logBuilder.AppendLine(condition);
                    break;
            }
        }

        private string CombineLogs()
        {
            if (_errorBuilder.Length == 0 && _logBuilder.Length == 0)
            {
                return string.Empty;
            }

            if (_errorBuilder.Length == 0)
            {
                return _logBuilder.ToString();
            }

            if (_logBuilder.Length == 0)
            {
                return _errorBuilder.ToString();
            }

            var combined = new StringBuilder();
            combined.AppendLine("Standard Log:");
            combined.AppendLine(_logBuilder.ToString().TrimEnd());
            combined.AppendLine("Errors Log:");
            combined.Append(_errorBuilder.ToString().TrimEnd());
            return combined.ToString();
        }
    }
}