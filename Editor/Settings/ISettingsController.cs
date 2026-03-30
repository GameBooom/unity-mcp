// Copyright (C) GameBooom. Licensed under MIT.

using System;

namespace GameBooom.Editor.Settings
{
    internal interface ISettingsController
    {
        bool MCPServerEnabled { get; set; }
        int MCPServerPort { get; set; }
        string MCPToolExportProfile { get; set; }

        event Action OnSettingsChanged;
    }
}
