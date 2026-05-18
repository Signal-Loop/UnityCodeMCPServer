using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityCodeMcpServer.Protocol;
using UnityEngine;
using UnityEngine.TestTools;

public class GetUnityGameViewWindowScreenshotToolPlayModeTests
{
    private static UniTask<ToolsCallResult> ExecuteWithEmptyArgumentsAsync(GetUnityGameViewWindowScreenshotTool tool)
    {
        MethodInfo executeMethod = typeof(GetUnityGameViewWindowScreenshotTool).GetMethod(nameof(GetUnityGameViewWindowScreenshotTool.ExecuteAsync));
        Assert.IsNotNull(executeMethod, "Could not find ExecuteAsync method.");

        ParameterInfo[] parameters = executeMethod.GetParameters();
        Assert.AreEqual(1, parameters.Length, "ExecuteAsync signature changed unexpectedly.");

        object emptyArguments = Activator.CreateInstance(parameters[0].ParameterType);
        object invocationResult = executeMethod.Invoke(tool, new[] { emptyArguments });
        Assert.IsNotNull(invocationResult, "ExecuteAsync returned null.");

        return (UniTask<ToolsCallResult>)invocationResult;
    }

    private static byte[] CreateTestPngBytes()
    {
        Texture2D texture = new(2, 2, TextureFormat.RGBA32, false);
        try
        {
            Color[] pixels =
            {
                Color.red, Color.green,
                Color.blue, Color.white
            };

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture.EncodeToPNG();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    [UnityTest]
    public IEnumerator ExecuteAsync_AllowsCaptureWhenTimeScaleIsZeroInPlayMode() => UniTask.ToCoroutine(async () =>
    {
        string tempPath = Path.Combine(Application.temporaryCachePath, $"paused-screenshot-{System.Guid.NewGuid():N}.png");
        byte[] pngBytes = CreateTestPngBytes();
        GetUnityGameViewWindowScreenshotTool tool = new(
            () => tempPath,
            path => File.WriteAllBytes(path, pngBytes),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(10));

        float originalTimeScale = Time.timeScale;
        try
        {
            Time.timeScale = 0f;

            ToolsCallResult result = await ExecuteWithEmptyArgumentsAsync(tool);

            Assert.IsFalse(result.IsError, result.Content.Count > 0 ? result.Content[0].Text : "Expected screenshot capture to succeed while paused.");
            Assert.IsNotEmpty(result.Content);
            Assert.IsNotNull(result.Content[0].Data);
        }
        finally
        {
            Time.timeScale = originalTimeScale;
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    });
}
