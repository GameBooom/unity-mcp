// Copyright (C) GameBooom. Licensed under MIT.

using UnityEditor;

namespace GameBooom.Editor.Services
{
    internal class EditorStateService : IEditorStateService
    {
        public bool IsPlayingOrWillChangePlaymode =>
            EditorApplication.isPlayingOrWillChangePlaymode;

        public bool IsCompiling => EditorApplication.isCompiling;
    }
}
