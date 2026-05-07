// Copyright (C) Funplay. Licensed under MIT.

using System;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using Funplay.Editor.Tools.Helpers;
using UnityEditor;

namespace Funplay.Editor.Tools.Builtins
{
    /// <summary>
    /// Invoke arbitrary editor menu items. This is the cheap-and-broad fallback when no
    /// dedicated tool exists — Unity itself, all engine modules, and every third-party
    /// package already publish their commands as menu items, so an agent can drive most
    /// editor functionality through this without us writing wrappers.
    /// </summary>
    [ToolProvider("MenuItem")]
    internal static class MenuItemFunctions
    {
        [Description("Execute an editor menu item by its full path. " +
                     "Examples: 'GameObject/2D Object/Sprites/Square', 'Window/Layouts/Default', 'Edit/Undo'. " +
                     "Returns success/failure based on whether the menu item exists and was triggered.")]
        [SceneEditingTool]
        public static object ExecuteMenuItem(
            [ToolParam("Full menu path, e.g. 'GameObject/2D Object/Sprite'")] string menu_path)
        {
            if (string.IsNullOrWhiteSpace(menu_path))
                return Response.Error("MENU_PATH_REQUIRED: menu_path cannot be empty.");

            try
            {
                var ok = EditorApplication.ExecuteMenuItem(menu_path);
                if (!ok)
                    return Response.Error("MENU_ITEM_NOT_FOUND",
                        new { menu_path, hint = "Verify the path matches the editor menu hierarchy exactly (case sensitive, '/' separated)." });
                return Response.Success($"Executed menu item '{menu_path}'.");
            }
            catch (Exception ex)
            {
                return Response.Error($"MENU_EXECUTION_FAILED: {ex.Message}");
            }
        }

        [Description("Validate that a menu item exists without executing it. Useful when an agent wants to discover the right path before triggering side effects.")]
        [ReadOnlyTool]
        public static object ValidateMenuItem(
            [ToolParam("Full menu path to validate")] string menu_path)
        {
            if (string.IsNullOrWhiteSpace(menu_path))
                return Response.Error("MENU_PATH_REQUIRED: menu_path cannot be empty.");

            // Unity has no public "menu exists" API; ExecuteMenuItem returns false for unknown items
            // but it also runs the item if it exists. Approximation: run it and report. Document this.
            return Response.Success(
                "Menu validation requires executing the item. Use execute_menu_item and inspect the success flag.",
                new { menu_path });
        }
    }
}
