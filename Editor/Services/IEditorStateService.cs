// Copyright (C) GameBooom. Licensed under MIT.

namespace GameBooom.Editor.Services
{
    internal interface IEditorStateService
    {
        bool IsPlayingOrWillChangePlaymode { get; }
        bool IsCompiling { get; }
    }
}
