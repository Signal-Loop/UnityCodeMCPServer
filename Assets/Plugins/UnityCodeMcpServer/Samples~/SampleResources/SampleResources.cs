using System.Collections.Generic;
using UnityCodeMcpServer.Interfaces;
using UnityCodeMcpServer.Protocol;
using UnityEngine;

namespace UnityCodeMcpServer.SampleTools
{
    /// <summary>
    /// Sample resource that exposes Unity project information
    /// </summary>
    public class ProjectInfoResource : IResource
    {
        public string Uri => "unity://project/info";

        public string Name => "Project Info";

        public string Description => "Information about the current Unity project";

        public string MimeType => "application/json";

        public ResourcesReadResult Read()
        {
            var info = new
            {
                productName = Application.productName,
                companyName = Application.companyName,
                version = Application.version,
                unityVersion = Application.unityVersion,
                dataPath = Application.dataPath,
                persistentDataPath = Application.persistentDataPath
            };

            return new ResourcesReadResult
            {
                Contents = new List<ResourceContent>
                {
                    new ResourceContent
                    {
                        Uri = Uri,
                        MimeType = MimeType,
                        Text = JsonHelper.Serialize(info, indented: true)
                    }
                }
            };
        }
    }

    /// <summary>
    /// Sample resource that exposes system information
    /// </summary>
    public class SystemInfoResource : IResource
    {
        public string Uri => "unity://system/info";

        public string Name => "System Info";

        public string Description => "Information about the system running Unity";

        public string MimeType => "application/json";

        public ResourcesReadResult Read()
        {
            var info = new
            {
                operatingSystem = SystemInfo.operatingSystem,
                processorType = SystemInfo.processorType,
                processorCount = SystemInfo.processorCount,
                systemMemorySize = SystemInfo.systemMemorySize,
                graphicsDeviceName = SystemInfo.graphicsDeviceName,
                graphicsMemorySize = SystemInfo.graphicsMemorySize,
                deviceModel = SystemInfo.deviceModel,
                deviceName = SystemInfo.deviceName
            };

            return new ResourcesReadResult
            {
                Contents = new List<ResourceContent>
                {
                    new ResourceContent
                    {
                        Uri = Uri,
                        MimeType = MimeType,
                        Text = JsonHelper.Serialize(info, indented: true)
                    }
                }
            };
        }
    }
}
