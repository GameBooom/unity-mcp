// Copyright (C) Funplay. Licensed under MIT.

using System;

namespace Funplay.Editor.Settings
{
    internal interface ISettingsController
    {
        bool MCPServerEnabled { get; set; }
        int MCPServerPort { get; set; }
        string MCPToolExportProfile { get; set; }
        bool MCPCoreToolsConfigured { get; }
        string[] MCPCoreTools { get; set; }
        bool MCPFullToolsConfigured { get; }
        string[] MCPFullTools { get; set; }
        string MCPSelectedConfigTarget { get; set; }
        bool PluginDebugLoggingEnabled { get; set; }

        event Action OnSettingsChanged;
    }
}
