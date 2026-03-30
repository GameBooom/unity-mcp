// Copyright (C) GameBooom. Licensed under MIT.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

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

                AppendUiClickResult(results, x, y, inputButton);
                AppendPhysicsClickResult(results, x, y);

                return $"Mouse {button} click at ({x}, {y}):\n{results}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        private static void AppendUiClickResult(StringBuilder results, int x, int y, PointerEventData.InputButton inputButton)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                results.AppendLine("  EventSystem: skipped (no EventSystem found)");
                return;
            }

            var pointerData = CreatePointerData(eventSystem, new Vector2(x, y), inputButton);
            if (!TryGetTopUiTarget(eventSystem, pointerData, out var target, out var raycast))
            {
                results.AppendLine("  EventSystem: no UI element at position");
                return;
            }

            pointerData.pointerCurrentRaycast = raycast;
            pointerData.pointerPressRaycast = raycast;
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerClickHandler);
            results.AppendLine($"  EventSystem: clicked on '{target.name}'");
        }

        private static void AppendPhysicsClickResult(StringBuilder results, int x, int y)
        {
            var mainCamera = Camera.main;
            if (mainCamera == null)
            {
                results.AppendLine("  Physics raycast: skipped (no Main Camera found)");
                return;
            }

            var ray = mainCamera.ScreenPointToRay(new Vector3(x, y, 0f));
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
