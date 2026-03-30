// Copyright (C) GameBooom. Licensed under MIT.

using System;
using UnityEditor;

namespace GameBooom.Editor.Settings
{
    internal class SettingsController : ISettingsController
    {
        private const string Prefix = "GameBooom_";

        public event Action OnSettingsChanged;

        public bool MCPServerEnabled
        {
            get => EditorPrefs.GetBool(Prefix + "MCPServerEnabled", false);
            set
            {
                EditorPrefs.SetBool(Prefix + "MCPServerEnabled", value);
                OnSettingsChanged?.Invoke();
            }
        }

        public int MCPServerPort
        {
            get => EditorPrefs.GetInt(Prefix + "MCPServerPort", 8765);
            set
            {
                EditorPrefs.SetInt(Prefix + "MCPServerPort", value);
                OnSettingsChanged?.Invoke();
            }
        }

        public string MCPToolExportProfile
        {
            get => EditorPrefs.GetString(Prefix + "MCPToolExportProfile", "core");
            set
            {
                EditorPrefs.SetString(Prefix + "MCPToolExportProfile", string.IsNullOrWhiteSpace(value) ? "core" : value);
                OnSettingsChanged?.Invoke();
            }
        }
    }
}
