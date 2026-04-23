using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Cysharp.Threading.Tasks;
using UnityCodeMcpServer.Helpers;
using UnityCodeMcpServer.Interfaces;
using UnityCodeMcpServer.Protocol;
using UnityEditor;
using UnityEngine;
/// <summary>
/// Tool that captures a screenshot of the Unity Game View and returns it as an MCP image content item.
/// </summary>
public class GetUnityGameViewWindowScreenshotTool : IToolAsync
{
    private const string DefaultMimeType = "image/png";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(50);
    private readonly Func<string> _createTempPath;
    private readonly Action<string> _requestScreenshot;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _pollInterval;

    public GetUnityGameViewWindowScreenshotTool()
        : this(null, null, null, null)
    {
    }

    public GetUnityGameViewWindowScreenshotTool(
        Func<string> createTempPath,
        Action<string> requestScreenshot,
        TimeSpan? timeout,
        TimeSpan? pollInterval)
    {
        _createTempPath = createTempPath ?? CreateTempScreenshotPath;
        _requestScreenshot = requestScreenshot ?? RequestScreenshot;
        _timeout = timeout ?? DefaultTimeout;
        _pollInterval = pollInterval ?? DefaultPollInterval;
    }

    public string Name => "get_unity_game_view_window_screenshot";

    public string Description =>
        "Returns a screenshot of the Unity Game View window. Supports optional scaling to fit within a maximum height while preserving aspect ratio.";

    public JsonElement InputSchema => JsonHelper.ParseElement(@"
        {
            ""type"": ""object"",
            ""properties"": {
                ""max_height"": {
                    ""type"": ""integer"",
                    ""minimum"": 1,
                    ""description"": ""Maximum pixel height for the returned screenshot. Taller images are proportionally scaled down to save token context limits. Default: 640."",
                    ""default"": 640
                }
            }
        }
        ");

    public async UniTask<ToolsCallResult> ExecuteAsync(JsonElement arguments)
    {
        if (EditorApplication.isPlaying && Time.timeScale == 0)
        {
            return ToolsCallResult.ErrorResult("Cannot capture Game View screenshot while timeScale is 0 in Play Mode. Try again once the game is running.");
        }

        if (!TryParseMaxHeight(arguments, out int maxHeight, out string parseError))
        {
            return ToolsCallResult.ErrorResult(parseError);
        }

        var captureResult = await CaptureGameViewScreenshotAsync();
        if (captureResult.IsError)
        {
            return ToolsCallResult.ErrorResult(captureResult.ErrorMessage ?? "Failed to capture Game View screenshot.");
        }

        if (string.IsNullOrWhiteSpace(captureResult.Base64Data))
        {
            return ToolsCallResult.ErrorResult("Failed to capture Game View screenshot: empty image data.");
        }

        var scaledResult = ScaleCaptureToMaxHeight(captureResult, maxHeight);
        if (scaledResult.IsError)
        {
            return ToolsCallResult.ErrorResult(scaledResult.ErrorMessage ?? "Failed to scale screenshot.");
        }

        var mimeType = string.IsNullOrWhiteSpace(scaledResult.MimeType) ? DefaultMimeType : scaledResult.MimeType;

        UnityCodeMcpServerLogger.Debug($"[GetUnityGameViewWindowScreenshotTool] [{Time.frameCount}]: Successfully captured and scaled screenshot. Data length: {scaledResult.Base64Data.Length}, MimeType: {mimeType}");

        return ToolsCallResult.ImageResult(scaledResult.Base64Data, mimeType);
    }

    private async UniTask<CaptureResult> CaptureGameViewScreenshotAsync()
    {
        string tempPath = null;
        try
        {
            RepaintGameView();
            UnityCodeMcpServerLogger.Debug($"[GetUnityGameViewWindowScreenshotTool] [{Time.frameCount}]: Repainted Game View.");
            tempPath = _createTempPath();
            UnityCodeMcpServerLogger.Debug($"[GetUnityGameViewWindowScreenshotTool] [{Time.frameCount}]: Created temp path: {tempPath}");
            _requestScreenshot(tempPath);

            var pngBytes = await ReadFileWhenReadyAsync(tempPath, _timeout, _pollInterval);
            UnityCodeMcpServerLogger.Debug($"[GetUnityGameViewWindowScreenshotTool] [{Time.frameCount}]: Read file bytes: {(pngBytes != null ? pngBytes.Length.ToString() : "null")}");
            if (pngBytes == null || pngBytes.Length == 0)
            {
                return CaptureResult.Error("Game View screenshot not ready. Ensure the Game View is visible and try again.");
            }

            var base64 = Convert.ToBase64String(pngBytes);
            return CaptureResult.Success(base64, DefaultMimeType);
        }
        catch (Exception ex)
        {
            return CaptureResult.Error($"Failed to capture Game View screenshot: {ex.Message}");
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    private static string CreateTempScreenshotPath()
    {
        var tempDir = Path.Combine(Application.dataPath, "..", "Temp", "UnityGameViewScreenshots");
        Directory.CreateDirectory(tempDir);
        return Path.Combine(tempDir, $"game_view_{Guid.NewGuid():N}.png");
    }

    private static void RequestScreenshot(string path)
    {
        ScreenCapture.CaptureScreenshot(path, 1);
        UnityCodeMcpServerLogger.Debug($"[GetUnityGameViewWindowScreenshotTool] [{Time.frameCount}]: Requested screenshot capture to path: {path}");
    }

    private static async UniTask<byte[]> ReadFileWhenReadyAsync(string path, TimeSpan timeout, TimeSpan pollInterval)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            if (File.Exists(path))
            {
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    if (bytes != null && bytes.Length > 0)
                    {
                        UnityCodeMcpServerLogger.Debug($"[GetUnityGameViewWindowScreenshotTool] [{Time.frameCount}]: Successfully read screenshot file: {path}, bytes length: {bytes.Length}");
                        return bytes;
                    }
                }
                catch
                {
                    // File may still be in use; continue polling.
                }
            }
            UnityCodeMcpServerLogger.Debug($"[GetUnityGameViewWindowScreenshotTool] [{Time.frameCount}]: awaiting screenshot file: {path}");
            await UniTask.Delay(pollInterval);
        }

        return null;
    }

    private static void TryDeleteTempFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    public static bool TryParseMaxHeight(JsonElement arguments, out int maxHeight, out string errorMessage)
    {
        maxHeight = 640;
        errorMessage = null;

        if (!arguments.TryGetProperty("max_height", out JsonElement element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out maxHeight))
        {
            errorMessage = "Parameter 'max_height' must be an integer.";
            return false;
        }

        if (maxHeight <= 0)
        {
            errorMessage = "Parameter 'max_height' must be greater than 0.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Calculates scaled dimensions to fit within max height while preserving aspect ratio.
    /// </summary>
    public static void GetScaledDimensionsToMaxHeight(int width, int height, int maxHeight, out int scaledWidth, out int scaledHeight)
    {
        if (width <= 0 || height <= 0)
        {
            scaledWidth = 1;
            scaledHeight = 1;
            return;
        }

        if (height <= maxHeight)
        {
            scaledWidth = width;
            scaledHeight = height;
            return;
        }

        double scale = (double)maxHeight / height;
        scaledWidth = Math.Max(1, (int)Math.Floor(width * scale));
        scaledHeight = maxHeight;
    }

    /// <summary>
    /// Scales a PNG image to target dimensions.
    /// </summary>
    private static CaptureResult ScalePngImage(byte[] source_bytes, int target_width, int target_height)
    {
        if (source_bytes == null || source_bytes.Length == 0)
        {
            return CaptureResult.Error("Empty screenshot bytes.");
        }

        Texture2D source_texture = new(2, 2, TextureFormat.RGB24, false);
        Texture2D scaled_texture = null;
        RenderTexture temporary_render_texture = null;
        RenderTexture previous_render_texture = RenderTexture.active;

        try
        {
            if (!source_texture.LoadImage(source_bytes, false))
            {
                return CaptureResult.Error("Failed to decode screenshot image.");
            }

            if (source_texture.width == target_width && source_texture.height == target_height)
            {
                // No scaling needed
                return CaptureResult.Success(Convert.ToBase64String(source_bytes), DefaultMimeType);
            }

            temporary_render_texture = RenderTexture.GetTemporary(target_width, target_height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source_texture, temporary_render_texture);
            RenderTexture.active = temporary_render_texture;

            scaled_texture = new Texture2D(target_width, target_height, TextureFormat.RGB24, false);
            scaled_texture.ReadPixels(new Rect(0, 0, target_width, target_height), 0, 0);
            scaled_texture.Apply(false, false);

            byte[] png_bytes = scaled_texture.EncodeToPNG();
            if (png_bytes == null || png_bytes.Length == 0)
            {
                return CaptureResult.Error("Failed to encode screenshot to PNG.");
            }

            return CaptureResult.Success(Convert.ToBase64String(png_bytes), DefaultMimeType);
        }
        finally
        {
            RenderTexture.active = previous_render_texture;

            if (temporary_render_texture != null)
            {
                RenderTexture.ReleaseTemporary(temporary_render_texture);
            }

            if (scaled_texture != null && !ReferenceEquals(scaled_texture, source_texture))
            {
                UnityEngine.Object.DestroyImmediate(scaled_texture);
            }

            UnityEngine.Object.DestroyImmediate(source_texture);
        }
    }

    private static CaptureResult ScaleCaptureToMaxHeight(CaptureResult captureResult, int maxHeight)
    {
        if (captureResult.IsError)
        {
            return captureResult;
        }

        if (string.IsNullOrWhiteSpace(captureResult.Base64Data))
        {
            return CaptureResult.Error("Captured screenshot data was empty.");
        }

        byte[] png_bytes;
        try
        {
            png_bytes = Convert.FromBase64String(captureResult.Base64Data);
        }
        catch (Exception ex)
        {
            return CaptureResult.Error($"Captured screenshot data was not valid base64: {ex.Message}");
        }

        Texture2D temp_texture = new(2, 2);
        try
        {
            if (!temp_texture.LoadImage(png_bytes, false))
            {
                return CaptureResult.Error("Failed to decode screenshot image.");
            }

            GetScaledDimensionsToMaxHeight(temp_texture.width, temp_texture.height, maxHeight, out int scaled_width, out int scaled_height);

            if (temp_texture.width == scaled_width && temp_texture.height == scaled_height)
            {
                return captureResult;
            }

            CaptureResult scaled_result = ScalePngImage(png_bytes, scaled_width, scaled_height);
            if (scaled_result.IsError)
            {
                return scaled_result;
            }

            return CaptureResult.Success(scaled_result.Base64Data, captureResult.MimeType ?? DefaultMimeType);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(temp_texture);
        }
    }

    private static void RepaintGameView()
    {
        try
        {
            var gameViewType = Type.GetType("UnityEditor.GameView, UnityEditor");
            if (gameViewType == null)
            {
                return;
            }

            var gameView = EditorWindow.GetWindow(gameViewType);
            if (gameView != null)
            {
                gameView.Repaint();
            }
        }
        catch
        {
            // Ignore repaint failures; capture will still attempt.
        }
    }

    public readonly struct CaptureResult
    {
        public bool IsError { get; }
        public string Base64Data { get; }
        public string MimeType { get; }
        public string ErrorMessage { get; }

        private CaptureResult(bool isError, string base64Data, string mimeType, string errorMessage)
        {
            IsError = isError;
            Base64Data = base64Data;
            MimeType = mimeType;
            ErrorMessage = errorMessage;
        }

        public static CaptureResult Success(string base64Data, string mimeType)
        {
            return new CaptureResult(false, base64Data, mimeType, null);
        }

        public static CaptureResult Error(string message)
        {
            return new CaptureResult(true, null, null, message);
        }
    }
}
