// Copyright (C) GameBooom. Licensed under MIT.

namespace GameBooom.Editor.Services
{
    internal interface IEditorContextBuilder
    {
        string GetContextBlock();
        string GetActiveSceneSummary();
        string GetSelectionSummary(int maxItems = 5);
        string GetConsoleErrorSummary(int count = 5);
        string GetCompileErrorContext(int maxEntries = 5, int snippetRadius = 3);
        string VerifyUnityChanges(
            bool checkCompilation = true,
            bool checkConsole = true,
            bool includeSceneInfo = true,
            int consoleCount = 5);
    }
}
