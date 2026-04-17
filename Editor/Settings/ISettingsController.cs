// Copyright (C) Funplay. Licensed under MIT.

using System;

namespace Funplay.Editor.Settings
{
    internal interface ISettingsController
    {
        bool MCPServerEnabled { get; set; }
        int MCPServerPort { get; set; }
        string MCPToolExportProfile { get; set; }
        string MCPSelectedConfigTarget { get; set; }

        event Action OnSettingsChanged;
    }
}
