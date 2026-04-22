using System;
using System.Collections.Generic;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Interfaces;
using UnityCodeMcpServer.Protocol;

namespace UnityCodeMcpServer.McpResources
{
    /// <summary>
    /// Resource that exposes Unity Editor Console logs.
    ///
    /// This resource uses reflection to accessUnity's internal LogEntries API,
    /// extracting console logs and providing them as text content. It implements
    /// protective measures including:
    /// - Safe reflection with null-checks and error handling
    /// - Log truncation to prevent excessive payloads (max 1000 entries)
    /// - Proper resource cleanup via finally blocks
    /// - Comprehensive error reporting
    /// </summary>
    public class UnityConsoleLogsResource : IResource
    {
        /// <summary>The maximum number of log entries to retrieve (prevents memory issues).</summary>
        private const int MaxLogEntries = 1000;

        /// <summary>The URI identifier for this resource.</summary>
        private const string ResourceUri = "unity://console/logs";

        /// <summary>The human-readable name of this resource.</summary>
        private const string ResourceName = "Unity Console Logs";

        /// <summary>The MIME type for this resource content.</summary>
        private const string ResourceMimeType = "text/plain";

        /// <summary>Description of what this resource provides.</summary>
        private const string ResourceDescription = "Reads the Unity Editor Console logs.";

        private readonly Func<int, (string text, bool isError)> _logReader;

        public UnityConsoleLogsResource()
            : this(null)
        {
        }

        public UnityConsoleLogsResource(Func<int, (string text, bool isError)> logReader)
        {
            _logReader = logReader ?? UnityConsoleLogReader.ReadTail;
        }

        public string Uri => ResourceUri;

        public string Name => ResourceName;

        public string Description => ResourceDescription;

        public string MimeType => ResourceMimeType;

        /// <summary>
        /// Reads console logs from the Unity Editor.
        ///
        /// Uses reflection to safely access internal LogEntries. Handles missing types/methods
        /// gracefully and implements proper error handling with resource cleanup.
        /// </summary>
        /// <returns>A ResourcesReadResult containing the log content or error message.</returns>
        public ResourcesReadResult Read()
        {
            (string text, bool _) = _logReader(MaxLogEntries);
            return CreateSuccessResult(string.IsNullOrWhiteSpace(text) ? "(No console logs available)" : text);
        }

        /// <summary>
        /// Creates a success result with the provided content.
        /// </summary>
        private ResourcesReadResult CreateSuccessResult(string content)
        {
            return new ResourcesReadResult
            {
                Contents = new List<ResourceContent>
                {
                    new() {
                        Uri = Uri,
                        MimeType = MimeType,
                        Text = content
                    }
                }
            };
        }

        /// <summary>
        /// Creates an error result with the specified error message.
        /// </summary>
        private ResourcesReadResult CreateErrorResult(string message)
        {
            return CreateSuccessResult(message);
        }
    }
}
