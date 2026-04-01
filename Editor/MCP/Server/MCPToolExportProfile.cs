// Copyright (C) GameBooom. Licensed under MIT.

using System;
using System.Collections.Generic;

namespace GameBooom.Editor.MCP.Server
{
    internal enum MCPToolExportProfile
    {
        Core,
        Full
    }

    internal static class MCPToolExportPolicy
    {
        private static readonly HashSet<string> CoreTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "execute_code",
            "simulate_key_press",
            "simulate_key_combo",
            "simulate_mouse_click",
            "simulate_mouse_drag",
            "get_scene_info",
            "get_hierarchy",
            "get_console_logs",
            "get_performance_snapshot",
            "analyze_scene_complexity",
            "capture_scene_view",
            "capture_game_view",
            "wait_for_compilation",
            "request_recompile",
            "get_compilation_errors",
            "get_reload_recovery_status",
            "enter_play_mode",
            "exit_play_mode",
            "get_time_scale"
        };

        public static MCPToolExportProfile Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return MCPToolExportProfile.Core;

            switch (value.Trim().ToLowerInvariant())
            {
                case "full":
                    return MCPToolExportProfile.Full;
                default:
                    return MCPToolExportProfile.Core;
            }
        }

        public static string ToSettingValue(MCPToolExportProfile profile)
        {
            switch (profile)
            {
                case MCPToolExportProfile.Full:
                    return "full";
                default:
                    return "core";
            }
        }

        public static bool IsToolAllowed(string toolName, MCPToolExportProfile profile)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return false;

            return profile == MCPToolExportProfile.Full || CoreTools.Contains(toolName);
        }

        public static string BuildDescriptionPrefix(MCPToolExportProfile profile)
        {
            return profile == MCPToolExportProfile.Core ? "[core] " : string.Empty;
        }

        public static int GetSortRank(string toolName, MCPToolExportProfile profile)
        {
            if (string.Equals(toolName, "execute_code", StringComparison.OrdinalIgnoreCase))
                return 0;

            if (profile == MCPToolExportProfile.Core && CoreTools.Contains(toolName))
                return 100;

            return 1000;
        }

        public static string BuildDescriptionPrefix(string toolName, MCPToolExportProfile profile)
        {
            if (string.Equals(toolName, "execute_code", StringComparison.OrdinalIgnoreCase))
                return "[primary] " + BuildDescriptionPrefix(profile);

            return BuildDescriptionPrefix(profile);
        }
    }
}
