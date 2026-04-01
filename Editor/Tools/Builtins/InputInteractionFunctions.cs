// Copyright (C) GameBooom. Licensed under MIT.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameBooom.Editor.Tools.Builtins
{
    [ToolProvider("InputSimulation")]
    internal static class InputInteractionFunctions
    {
        [Description("Simulate a mouse click at a screen position in Play Mode. Coordinates are in screen pixels with 0,0 at the bottom-left. Works without the Input System by dispatching UI and physics events.")]
        [ReadOnlyTool]
        public static string SimulateMouseClick(
            [ToolParam("Screen X coordinate in pixels")] int x,
            [ToolParam("Screen Y coordinate in pixels")] int y,
            [ToolParam("Mouse button: left, right, or middle", Required = false)] string button = "left")
        {
            if (!EditorApplication.isPlaying)
                return "Error: SimulateMouseClick only works in Play Mode.";

            try
            {
                var results = new StringBuilder();
                var inputButton = ParseButton(button);
                var uiPosition = ResolveUiClickPosition(x, y, out var uiMessage);
                var viewportPosition = ResolveViewportClickPosition(x, y, out var viewportMessage);

                if (!string.IsNullOrEmpty(uiMessage))
                    results.AppendLine(uiMessage);
                if (!string.IsNullOrEmpty(viewportMessage))
                    results.AppendLine(viewportMessage);

                AppendUiClickResult(results, Mathf.RoundToInt(uiPosition.x), Mathf.RoundToInt(uiPosition.y), inputButton);
                AppendPhysicsClickResult(results, viewportPosition);

                return $"Mouse {button} click at ({x}, {y}) -> UI({uiPosition.x:F1}, {uiPosition.y:F1}) viewport({viewportPosition.x:F3}, {viewportPosition.y:F3}):\n{results}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        private static Vector2 ResolveUiClickPosition(float x, float y, out string message)
        {
            message = null;

            if (!TryGetGameViewRenderSize(out var renderSize))
                return new Vector2(x, y);

            if (renderSize.x <= 0f || renderSize.y <= 0f)
                return new Vector2(x, y);

            var uiX = Mathf.Clamp(x, 0f, renderSize.x);
            var uiY = Mathf.Clamp(y, 0f, renderSize.y);
            message = $"  UI mapping: input ({x:F1}, {y:F1}) in render {renderSize.x:F0}x{renderSize.y:F0} -> ({uiX:F1}, {uiY:F1})";
            return new Vector2(uiX, uiY);
        }

        private static Vector2 ResolveViewportClickPosition(float x, float y, out string message)
        {
            message = null;

            if (!TryGetGameViewRenderSize(out var renderSize) || renderSize.x <= 0f || renderSize.y <= 0f)
                return new Vector2(0.5f, 0.5f);

            var viewport = new Vector2(
                Mathf.Clamp01(x / Mathf.Max(1f, renderSize.x)),
                Mathf.Clamp01(y / Mathf.Max(1f, renderSize.y)));
            message = $"  Physics mapping: input ({x:F1}, {y:F1}) in render {renderSize.x:F0}x{renderSize.y:F0} -> viewport ({viewport.x:F3}, {viewport.y:F3})";
            return viewport;
        }

        private static void AppendUiClickResult(StringBuilder results, int x, int y, PointerEventData.InputButton inputButton)
        {
            Canvas.ForceUpdateCanvases();

            var eventSystem = EventSystem.current ?? UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                results.AppendLine("  EventSystem: skipped (no EventSystem found)");
                AppendDirectButtonFallback(results, new Vector2(x, y));
                return;
            }

            var pointerData = CreatePointerData(eventSystem, new Vector2(x, y), inputButton);
            if (!TryGetTopUiTarget(eventSystem, pointerData, out var target, out var raycast))
            {
                results.AppendLine("  EventSystem: no UI element at position");
                AppendDirectButtonFallback(results, new Vector2(x, y));
                return;
            }

            pointerData.pointerCurrentRaycast = raycast;
            pointerData.pointerPressRaycast = raycast;
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerClickHandler);
            results.AppendLine($"  EventSystem: clicked on '{target.name}'");

            if (target.TryGetComponent<Button>(out var button))
                button.onClick?.Invoke();
        }

        private static void AppendDirectButtonFallback(StringBuilder results, Vector2 position)
        {
            foreach (var button in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (button == null || !button.isActiveAndEnabled)
                    continue;

                var rectTransform = button.GetComponent<RectTransform>();
                if (rectTransform == null)
                    continue;

                if (!RectTransformUtility.RectangleContainsScreenPoint(rectTransform, position, null))
                    continue;

                button.onClick?.Invoke();
                results.AppendLine($"  Direct button hit: invoked '{button.name}'");
                return;
            }
        }

        private static void AppendPhysicsClickResult(StringBuilder results, Vector2 viewportPosition)
        {
            var mainCamera = Camera.main;
            if (mainCamera == null)
            {
                results.AppendLine("  Physics raycast: skipped (no Main Camera found)");
                return;
            }

            Physics.SyncTransforms();
            var ray = mainCamera.ViewportPointToRay(new Vector3(viewportPosition.x, viewportPosition.y, 0f));
            if (Physics.Raycast(ray, out var hit, 1000f))
            {
                hit.collider.gameObject.SendMessage("OnMouseDown", SendMessageOptions.DontRequireReceiver);
                hit.collider.gameObject.SendMessage("OnMouseUp", SendMessageOptions.DontRequireReceiver);
                results.AppendLine($"  Physics raycast: OnMouseDown on '{hit.collider.gameObject.name}'");
            }
            else
            {
                results.AppendLine("  Physics raycast: no 3D object hit");
            }
        }

        private static PointerEventData CreatePointerData(EventSystem eventSystem, Vector2 position, PointerEventData.InputButton inputButton)
        {
            return new PointerEventData(eventSystem)
            {
                position = position,
                pressPosition = position,
                button = inputButton
            };
        }

        private static bool TryGetTopUiTarget(
            EventSystem eventSystem,
            PointerEventData pointerData,
            out GameObject target,
            out RaycastResult raycast)
        {
            var raycastResults = new List<RaycastResult>();
            eventSystem.RaycastAll(pointerData, raycastResults);

            if (raycastResults.Count == 0)
            {
                target = null;
                raycast = default;
                return false;
            }

            raycast = raycastResults[0];
            target = raycast.gameObject;
            return target != null;
        }

        private static bool TryGetGameViewRenderSize(out Vector2 size)
        {
            size = default;

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

                if (value is Vector2 vector2)
                {
                    size = vector2;
                    return size.x > 0f && size.y > 0f;
                }
            }
            catch
            {
            }

            return false;
        }

        private static PointerEventData.InputButton ParseButton(string button)
        {
            switch ((button ?? "left").Trim().ToLowerInvariant())
            {
                case "right":
                    return PointerEventData.InputButton.Right;
                case "middle":
                    return PointerEventData.InputButton.Middle;
                default:
                    return PointerEventData.InputButton.Left;
            }
        }
    }
}
