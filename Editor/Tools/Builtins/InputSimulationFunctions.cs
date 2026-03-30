// Copyright (C) GameBooom. Licensed under MIT.

#if ENABLE_INPUT_SYSTEM
using System;
using System.ComponentModel;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace GameBooom.Editor.Tools.Builtins
{
    [ToolProvider("InputSimulation")]
    internal static class InputSimulationFunctions
    {
        [Description("Simulate keyboard input in Play Mode. Queues key events that the Unity Input System will process. Supports key names like W, A, S, D, Space, LeftShift, E, Q, and number keys.")]
        [ReadOnlyTool]
        public static string SimulateKeyPress(
            [ToolParam("Key name, for example W, Space, LeftShift, E, or 1")] string key,
            [ToolParam("Duration in seconds to hold the key. Use 0 for a tap.", Required = false)] float duration = 0f,
            [ToolParam("Action type: press, release, or tap", Required = false)] string action = "tap")
        {
            if (!EditorApplication.isPlaying)
                return "Error: SimulateKeyPress only works in Play Mode.";

            try
            {
                var keyboard = EnsureKeyboard();
                if (keyboard == null)
                    return "Error: No keyboard device found in Input System";

                var keyControl = FindKey(keyboard, key);
                if (keyControl == null)
                    return $"Error: Key '{key}' not recognized. Examples: W, A, S, D, Space, LeftShift, E, Escape, 1, 2";

                switch ((action ?? "tap").Trim().ToLowerInvariant())
                {
                    case "press":
                        QueueKeyState(keyboard, keyControl, true);
                        return $"Key '{key}' pressed (held down)";
                    case "release":
                        QueueKeyState(keyboard, keyControl, false);
                        return $"Key '{key}' released";
                    default:
                        return TapKey(keyboard, keyControl, key, duration);
                }
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        [Description("Simulate multiple keys held simultaneously in Play Mode, for example W plus LeftShift.")]
        [ReadOnlyTool]
        public static string SimulateKeyCombo(
            [ToolParam("Comma-separated key names, for example 'W,LeftShift' or 'A,Space'")] string keys,
            [ToolParam("Duration in seconds to hold the combo", Required = false)] float duration = 0.5f)
        {
            if (!EditorApplication.isPlaying)
                return "Error: SimulateKeyCombo only works in Play Mode.";

            try
            {
                var keyboard = EnsureKeyboard();
                if (keyboard == null)
                    return "Error: No keyboard device found";

                duration = Mathf.Clamp(duration, 0.05f, 5f);
                var keyNames = (keys ?? string.Empty).Split(',');

                for (int i = 0; i < keyNames.Length; i++)
                {
                    var keyControl = FindKey(keyboard, keyNames[i].Trim());
                    if (keyControl != null)
                        QueueKeyState(keyboard, keyControl, true);
                }

                double releaseTime = EditorApplication.timeSinceStartup + duration;
                EditorApplication.CallbackFunction releaseAll = null;
                releaseAll = () =>
                {
                    if (EditorApplication.timeSinceStartup < releaseTime)
                        return;

                    EditorApplication.update -= releaseAll;
                    var kb = Keyboard.current;
                    if (kb == null)
                        return;

                    for (int i = 0; i < keyNames.Length; i++)
                    {
                        var keyControl = FindKey(kb, keyNames[i].Trim());
                        if (keyControl != null)
                            QueueKeyState(kb, keyControl, false);
                    }
                };
                EditorApplication.update += releaseAll;

                return $"Keys [{keys}] held for {duration:F2}s";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        [Description("Simulate a mouse drag from one screen position to another in Play Mode using the Unity Input System.")]
        [ReadOnlyTool]
        public static string SimulateMouseDrag(
            [ToolParam("Start X coordinate in pixels")] int start_x,
            [ToolParam("Start Y coordinate in pixels")] int start_y,
            [ToolParam("End X coordinate in pixels")] int end_x,
            [ToolParam("End Y coordinate in pixels")] int end_y,
            [ToolParam("Duration of the drag in seconds", Required = false)] float duration = 0.5f,
            [ToolParam("Mouse button: left, right, or middle", Required = false)] string button = "left")
        {
            if (!EditorApplication.isPlaying)
                return "Error: SimulateMouseDrag only works in Play Mode.";

            try
            {
                var mouse = EnsureMouse();
                if (mouse == null)
                    return "Error: No mouse device found in Input System";

                duration = Mathf.Clamp(duration, 0.1f, 3f);
                var pressButton = GetMouseButton(mouse, button);

                InputState.Change(mouse.position, new Vector2(start_x, start_y));
                using (StateEvent.From(mouse, out var pressEvent))
                {
                    mouse.position.WriteValueIntoEvent(new Vector2(start_x, start_y), pressEvent);
                    pressButton.WriteValueIntoEvent(1f, pressEvent);
                    InputSystem.QueueEvent(pressEvent);
                }

                int steps = Mathf.Max(5, Mathf.RoundToInt(duration * 30));
                for (int i = 1; i < steps; i++)
                {
                    float t = (float)i / steps;
                    float curX = Mathf.Lerp(start_x, end_x, t);
                    float curY = Mathf.Lerp(start_y, end_y, t);

                    using (StateEvent.From(mouse, out var moveEvent))
                    {
                        mouse.position.WriteValueIntoEvent(new Vector2(curX, curY), moveEvent);
                        pressButton.WriteValueIntoEvent(1f, moveEvent);
                        InputSystem.QueueEvent(moveEvent);
                    }
                }

                using (StateEvent.From(mouse, out var releaseEvent))
                {
                    mouse.position.WriteValueIntoEvent(new Vector2(end_x, end_y), releaseEvent);
                    InputSystem.QueueEvent(releaseEvent);
                }

                return $"Mouse drag from ({start_x},{start_y}) to ({end_x},{end_y}) ({steps} steps queued)";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        private static string TapKey(Keyboard keyboard, KeyControl keyControl, string key, float duration)
        {
            if (duration <= 0f)
            {
                QueueKeyState(keyboard, keyControl, true);
                EditorApplication.CallbackFunction releaseCallback = null;
                int frameToRelease = Time.frameCount + 2;
                releaseCallback = () =>
                {
                    if (Time.frameCount < frameToRelease)
                        return;

                    EditorApplication.update -= releaseCallback;
                    if (Keyboard.current != null)
                        QueueKeyState(Keyboard.current, FindKey(Keyboard.current, key), false);
                };
                EditorApplication.update += releaseCallback;
                return $"Key '{key}' tapped (1 frame)";
            }

            duration = Mathf.Clamp(duration, 0.01f, 5f);
            QueueKeyState(keyboard, keyControl, true);

            double releaseTime = EditorApplication.timeSinceStartup + duration;
            EditorApplication.CallbackFunction releaseAfterDuration = null;
            releaseAfterDuration = () =>
            {
                if (EditorApplication.timeSinceStartup < releaseTime)
                    return;

                EditorApplication.update -= releaseAfterDuration;
                if (Keyboard.current != null)
                    QueueKeyState(Keyboard.current, FindKey(Keyboard.current, key), false);
            };
            EditorApplication.update += releaseAfterDuration;
            return $"Key '{key}' held for {duration:F2}s";
        }

        private static void QueueKeyState(Keyboard keyboard, KeyControl keyControl, bool pressed)
        {
            if (keyboard == null || keyControl == null)
                return;

            using (StateEvent.From(keyboard, out var eventPtr))
            {
                keyControl.WriteValueIntoEvent(pressed ? 1f : 0f, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }
        }

        private static Keyboard EnsureKeyboard()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
                return keyboard;

            try
            {
                keyboard = InputSystem.GetDevice<Keyboard>();
                if (keyboard != null)
                    return keyboard;

                return InputSystem.AddDevice<Keyboard>();
            }
            catch
            {
                return null;
            }
        }

        private static Mouse EnsureMouse()
        {
            var mouse = Mouse.current;
            if (mouse != null)
                return mouse;

            try
            {
                mouse = InputSystem.GetDevice<Mouse>();
                if (mouse != null)
                    return mouse;

                return InputSystem.AddDevice<Mouse>();
            }
            catch
            {
                return null;
            }
        }

        private static KeyControl FindKey(Keyboard keyboard, string keyName)
        {
            if (keyboard == null || string.IsNullOrWhiteSpace(keyName))
                return null;

            try
            {
                var control = keyboard[keyName.ToLowerInvariant()] as KeyControl;
                if (control != null)
                    return control;
            }
            catch
            {
            }

            switch (keyName.Trim().ToLowerInvariant())
            {
                case "w": return keyboard.wKey;
                case "a": return keyboard.aKey;
                case "s": return keyboard.sKey;
                case "d": return keyboard.dKey;
                case "e": return keyboard.eKey;
                case "q": return keyboard.qKey;
                case "r": return keyboard.rKey;
                case "f": return keyboard.fKey;
                case "space": return keyboard.spaceKey;
                case "leftshift":
                case "lshift":
                case "shift":
                    return keyboard.leftShiftKey;
                case "rightshift":
                case "rshift":
                    return keyboard.rightShiftKey;
                case "leftctrl":
                case "lctrl":
                case "ctrl":
                    return keyboard.leftCtrlKey;
                case "leftalt":
                case "lalt":
                case "alt":
                    return keyboard.leftAltKey;
                case "tab": return keyboard.tabKey;
                case "escape":
                case "esc":
                    return keyboard.escapeKey;
                case "enter":
                case "return":
                    return keyboard.enterKey;
                case "backspace":
                    return keyboard.backspaceKey;
                case "1": return keyboard.digit1Key;
                case "2": return keyboard.digit2Key;
                case "3": return keyboard.digit3Key;
                case "4": return keyboard.digit4Key;
                case "5": return keyboard.digit5Key;
                case "6": return keyboard.digit6Key;
                case "7": return keyboard.digit7Key;
                case "8": return keyboard.digit8Key;
                case "9": return keyboard.digit9Key;
                case "0": return keyboard.digit0Key;
                default:
                    return null;
            }
        }

        private static ButtonControl GetMouseButton(Mouse mouse, string button)
        {
            switch ((button ?? "left").Trim().ToLowerInvariant())
            {
                case "right":
                    return mouse.rightButton;
                case "middle":
                    return mouse.middleButton;
                default:
                    return mouse.leftButton;
            }
        }
    }
}
#endif
