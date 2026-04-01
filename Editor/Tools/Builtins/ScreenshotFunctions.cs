// Copyright (C) GameBooom. Licensed under MIT.
using System;
using System.Collections.Generic;
using System.Reflection;

using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using UnityEditor;
using UnityEngine;

namespace GameBooom.Editor.Tools.Builtins
{
    [ToolProvider("Screenshot")]
    internal static class ScreenshotFunctions
    {
        private const string ImagePrefix = "data:image/png;base64,";

        [Description("Capture a screenshot of the Game View (what the main camera sees). Returns a base64-encoded PNG image.")]
        [ReadOnlyTool]
        public static string CaptureGameView(
            [ToolParam("Width of the screenshot in pixels", Required = false)] int width = 0,
            [ToolParam("Height of the screenshot in pixels", Required = false)] int height = 0)
        {
            if (!TryResolveGameViewSize(ref width, ref height))
            {
                width = Mathf.Clamp(width > 0 ? width : 512, 64, 4096);
                height = Mathf.Clamp(height > 0 ? height : 512, 64, 4096);
            }

            var camera = Camera.main;
            if (camera == null)
                camera = UnityEngine.Object.FindFirstObjectByType<Camera>();

            if (camera == null)
                return "Error: No camera found in the scene. Add a Camera component to capture the Game View.";

            return CaptureWithUI(camera, width, height);
        }

        [Description("Capture a screenshot of the Scene View (the editor's scene camera perspective). Returns a base64-encoded PNG image.")]
        [ReadOnlyTool]
        public static string CaptureSceneView(
            [ToolParam("Width of the screenshot in pixels", Required = false)] int width = 0,
            [ToolParam("Height of the screenshot in pixels", Required = false)] int height = 0)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                return "Error: No Scene View is currently open. Open a Scene View window first.";

            var camera = sceneView.camera;
            if (camera == null)
                return "Error: Scene View camera is not available.";

            if (width <= 0 || height <= 0)
            {
                width = Mathf.RoundToInt(camera.pixelWidth);
                height = Mathf.RoundToInt(camera.pixelHeight);
            }

            width = Mathf.Clamp(width, 64, 4096);
            height = Mathf.Clamp(height, 64, 4096);

            return CaptureFromCamera(camera, width, height);
        }

        private static bool TryResolveGameViewSize(ref int width, ref int height)
        {
            if (width > 0 && height > 0)
            {
                width = Mathf.Clamp(width, 64, 4096);
                height = Mathf.Clamp(height, 64, 4096);
                return true;
            }

            try
            {
                var playModeViewType = Type.GetType("UnityEditor.PlayModeView,UnityEditor");
                var getMainPlayModeView = playModeViewType?.GetMethod(
                    "GetMainPlayModeView",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var playModeView = getMainPlayModeView?.Invoke(null, null);
                if (playModeView == null)
                    return false;

                var targetRenderSizeProperty = playModeView.GetType().GetProperty(
                    "targetRenderSize",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var targetSizeProperty = playModeView.GetType().GetProperty(
                    "targetSize",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                var value = targetRenderSizeProperty?.GetValue(playModeView, null)
                           ?? targetSizeProperty?.GetValue(playModeView, null);

                if (value is Vector2 vector2 && vector2.x > 0f && vector2.y > 0f)
                {
                    width = Mathf.Clamp(Mathf.RoundToInt(vector2.x), 64, 4096);
                    height = Mathf.Clamp(Mathf.RoundToInt(vector2.y), 64, 4096);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        /// <summary>
        /// Captures the game view including ScreenSpaceOverlay UI by temporarily
        /// switching overlay canvases to ScreenSpaceCamera during render.
        /// </summary>
        private static string CaptureWithUI(Camera camera, int width, int height)
        {
            RenderTexture renderTexture = null;
            RenderTexture previousTarget = null;
            RenderTexture previousActive = null;
            Texture2D screenshot = null;
            var overlayCanvases = new List<Canvas>();

            try
            {
                renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                renderTexture.Create();

                // Find all ScreenSpaceOverlay canvases and temporarily switch to ScreenSpaceCamera
                var allCanvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
                foreach (var canvas in allCanvases)
                {
                    if (canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.gameObject.activeInHierarchy)
                    {
                        overlayCanvases.Add(canvas);
                        canvas.renderMode = RenderMode.ScreenSpaceCamera;
                        canvas.worldCamera = camera;
                        canvas.planeDistance = camera.nearClipPlane + 0.1f;
                    }
                }

                previousTarget = camera.targetTexture;
                previousActive = RenderTexture.active;

                camera.targetTexture = renderTexture;
                camera.Render();

                RenderTexture.active = renderTexture;
                screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
                screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                screenshot.Apply();

                var pngBytes = screenshot.EncodeToPNG();
                var base64 = Convert.ToBase64String(pngBytes);

                return ImagePrefix + base64;
            }
            catch (Exception ex)
            {
                return $"Error: Failed to capture screenshot: {ex.Message}";
            }
            finally
            {
                // Restore overlay canvases
                foreach (var canvas in overlayCanvases)
                {
                    if (canvas != null)
                    {
                        canvas.worldCamera = null;
                        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    }
                }

                if (camera != null)
                    camera.targetTexture = previousTarget;

                RenderTexture.active = previousActive;

                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
                if (screenshot != null)
                    UnityEngine.Object.DestroyImmediate(screenshot);
            }
        }

        private static string CaptureFromCamera(Camera camera, int width, int height)
        {
            RenderTexture renderTexture = null;
            RenderTexture previousTarget = null;
            RenderTexture previousActive = null;
            Texture2D screenshot = null;

            try
            {
                renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                renderTexture.Create();

                previousTarget = camera.targetTexture;
                previousActive = RenderTexture.active;

                camera.targetTexture = renderTexture;
                camera.Render();

                RenderTexture.active = renderTexture;
                screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
                screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                screenshot.Apply();

                var pngBytes = screenshot.EncodeToPNG();
                var base64 = Convert.ToBase64String(pngBytes);

                return ImagePrefix + base64;
            }
            catch (Exception ex)
            {
                return $"Error: Failed to capture screenshot: {ex.Message}";
            }
            finally
            {
                if (camera != null)
                    camera.targetTexture = previousTarget;

                RenderTexture.active = previousActive;

                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
                if (screenshot != null)
                    UnityEngine.Object.DestroyImmediate(screenshot);
            }
        }
    }
}
