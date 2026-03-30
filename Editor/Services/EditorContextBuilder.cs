// Copyright (C) GameBooom. Licensed under MIT.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GameBooom.Editor.Services.UnityLogs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameBooom.Editor.Services
{
    internal class EditorContextBuilder : IEditorContextBuilder, IDisposable
    {
        [Flags]
        private enum RefreshFlags
        {
            None = 0,
            Scene = 1 << 0,
            Selection = 1 << 1,
            Console = 1 << 2,
            Compile = 1 << 3,
            All = Scene | Selection | Console | Compile
        }

        private readonly object _lock = new object();
        private readonly ICompilationService _compilationService;
        private readonly UnityLogsRepository _unityLogsRepository;
        private readonly IApplicationPaths _applicationPaths;

        private string _cachedContext = string.Empty;
        private string _cachedSceneSummary = "Scene context unavailable.";
        private string _cachedSelectionSummary = "No active selection.";
        private string _cachedConsoleErrorSummary = "No recent console errors.";
        private string _cachedCompileErrorContext = "No compilation errors detected.";
        private RefreshFlags _dirtyFlags = RefreshFlags.All;
        private bool _refreshScheduled;
        private bool _disposed;

        public EditorContextBuilder(
            ICompilationService compilationService,
            UnityLogsRepository unityLogsRepository,
            IApplicationPaths applicationPaths)
        {
            _compilationService = compilationService;
            _unityLogsRepository = unityLogsRepository;
            _applicationPaths = applicationPaths;

            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorApplication.projectChanged += OnProjectChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorSceneManager.newSceneCreated += OnNewSceneCreated;

            _unityLogsRepository?.StartListening();
            RequestRefresh(RefreshFlags.All);
        }

        public string GetContextBlock()
        {
            EnsureFresh();
            lock (_lock)
                return _cachedContext;
        }

        public string GetActiveSceneSummary()
        {
            EnsureFresh();
            lock (_lock)
                return _cachedSceneSummary;
        }

        public string GetSelectionSummary(int maxItems = 5)
        {
            EnsureFresh();
            if (maxItems != 5)
                return BuildSelectionSummary(maxItems);

            lock (_lock)
                return _cachedSelectionSummary;
        }

        public string GetConsoleErrorSummary(int count = 5)
        {
            EnsureFresh();
            if (count != 5)
                return BuildConsoleErrorSummary(count);

            lock (_lock)
                return _cachedConsoleErrorSummary;
        }

        public string GetCompileErrorContext(int maxEntries = 5, int snippetRadius = 3)
        {
            EnsureFresh();
            if (maxEntries != 5 || snippetRadius != 3)
                return BuildCompileErrorContext(maxEntries, snippetRadius);

            lock (_lock)
                return _cachedCompileErrorContext;
        }

        public string VerifyUnityChanges(
            bool checkCompilation = true,
            bool checkConsole = true,
            bool includeSceneInfo = true,
            int consoleCount = 5)
        {
            var blocks = new List<string>();

            if (checkCompilation)
                blocks.Add("Compilation:\n" + GetCompileErrorContext());

            if (checkConsole)
                blocks.Add("Console:\n" + GetConsoleErrorSummary(consoleCount));

            if (includeSceneInfo)
                blocks.Add("Scene:\n" + GetActiveSceneSummary());

            return string.Join("\n\n", blocks);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            EditorSceneManager.newSceneCreated -= OnNewSceneCreated;
        }

        private void OnSelectionChanged()
        {
            RequestRefresh(RefreshFlags.Selection);
        }

        private void OnHierarchyChanged()
        {
            RequestRefresh(RefreshFlags.Scene | RefreshFlags.Selection);
        }

        private void OnProjectChanged()
        {
            RequestRefresh(RefreshFlags.Scene | RefreshFlags.Compile);
        }

        private void OnPlayModeStateChanged(PlayModeStateChange _)
        {
            RequestRefresh(RefreshFlags.Scene | RefreshFlags.Console);
        }

        private void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene _, UnityEngine.SceneManagement.Scene __)
        {
            RequestRefresh(RefreshFlags.Scene | RefreshFlags.Selection);
        }

        private void OnSceneOpened(UnityEngine.SceneManagement.Scene _, OpenSceneMode __)
        {
            RequestRefresh(RefreshFlags.Scene | RefreshFlags.Selection);
        }

        private void OnSceneSaved(UnityEngine.SceneManagement.Scene _)
        {
            RequestRefresh(RefreshFlags.Scene);
        }

        private void OnNewSceneCreated(UnityEngine.SceneManagement.Scene _, NewSceneSetup __, NewSceneMode ___)
        {
            RequestRefresh(RefreshFlags.Scene | RefreshFlags.Selection);
        }

        private void EnsureFresh()
        {
            if (_disposed)
                return;

            if (_dirtyFlags == RefreshFlags.None && !string.IsNullOrEmpty(_cachedContext))
                return;

            RefreshSnapshot();
        }

        private void RequestRefresh(RefreshFlags flags)
        {
            if (_disposed)
                return;

            _dirtyFlags |= flags;
            if (_refreshScheduled)
                return;

            _refreshScheduled = true;
            EditorApplication.delayCall += RefreshSnapshot;
        }

        private void RefreshSnapshot()
        {
            if (_disposed)
                return;

            _refreshScheduled = false;

            var flags = _dirtyFlags;
            if (flags == RefreshFlags.None && !string.IsNullOrEmpty(_cachedContext))
                return;

            var sceneSummary = (flags & RefreshFlags.Scene) != 0 ? BuildSceneSummary() : _cachedSceneSummary;
            var selectionSummary = (flags & RefreshFlags.Selection) != 0 ? BuildSelectionSummary(5) : _cachedSelectionSummary;
            var consoleSummary = (flags & RefreshFlags.Console) != 0 ? BuildConsoleErrorSummary(5) : _cachedConsoleErrorSummary;
            var compileSummary = (flags & RefreshFlags.Compile) != 0 ? BuildCompileErrorContext(5, 3) : _cachedCompileErrorContext;
            var context = BuildSnapshot(sceneSummary, selectionSummary, consoleSummary, compileSummary);

            lock (_lock)
            {
                _cachedSceneSummary = sceneSummary;
                _cachedSelectionSummary = selectionSummary;
                _cachedConsoleErrorSummary = consoleSummary;
                _cachedCompileErrorContext = compileSummary;
                _cachedContext = context;
                _dirtyFlags = RefreshFlags.None;
            }
        }

        private static string BuildSnapshot(
            string sceneSummary,
            string selectionSummary,
            string consoleSummary,
            string compileSummary)
        {
            var scene = EditorSceneManager.GetActiveScene();
            var sb = new StringBuilder();
            sb.AppendLine("## Live Editor Context");
            sb.AppendLine($"- Scene: {scene.name} ({(string.IsNullOrEmpty(scene.path) ? "unsaved" : scene.path)})");
            sb.AppendLine($"- Play Mode: {(EditorApplication.isPlaying ? "Playing" : "Edit Mode")}");
            sb.AppendLine($"- Compiling: {EditorApplication.isCompiling}");
            sb.AppendLine();
            sb.AppendLine("### Scene");
            sb.AppendLine(TrimLines(sceneSummary, 20));
            sb.AppendLine();
            sb.AppendLine("### Selection");
            sb.AppendLine(TrimLines(selectionSummary, 10));
            sb.AppendLine();
            sb.AppendLine("### Compilation");
            sb.AppendLine(TrimLines(compileSummary, 12));
            sb.AppendLine();
            sb.AppendLine("### Console Errors");
            sb.AppendLine(TrimLines(consoleSummary, 12));
            return sb.ToString().Trim();
        }

        private static string BuildSceneSummary()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var rootObjects = scene.GetRootGameObjects();
            var sb = new StringBuilder();

            sb.AppendLine($"Scene: {scene.name}");
            sb.AppendLine($"Path: {(string.IsNullOrEmpty(scene.path) ? "(unsaved scene)" : scene.path)}");
            sb.AppendLine($"Is Dirty: {scene.isDirty}");
            sb.AppendLine($"Root Objects: {rootObjects.Length}");
            sb.AppendLine("Hierarchy:");

            if (rootObjects.Length == 0)
            {
                sb.AppendLine("- (empty)");
                return sb.ToString().Trim();
            }

            for (int i = 0; i < rootObjects.Length; i++)
                AppendHierarchy(sb, rootObjects[i].transform, 0, 3);

            return sb.ToString().Trim();
        }

        private static void AppendHierarchy(StringBuilder sb, Transform transform, int depth, int maxDepth)
        {
            var indent = new string(' ', depth * 2);
            sb.Append(indent);
            sb.Append("- ");
            sb.Append(transform.name);

            if (!transform.gameObject.activeSelf)
                sb.Append(" [inactive]");

            var components = GetComponentSummary(transform.gameObject);
            if (!string.IsNullOrEmpty(components))
                sb.Append(" [" + components + "]");

            sb.AppendLine();

            if (depth >= maxDepth)
            {
                if (transform.childCount > 0)
                    sb.AppendLine(new string(' ', (depth + 1) * 2) + $"- ... ({transform.childCount} children)");
                return;
            }

            for (int i = 0; i < transform.childCount; i++)
                AppendHierarchy(sb, transform.GetChild(i), depth + 1, maxDepth);
        }

        private static string GetComponentSummary(GameObject gameObject)
        {
            var components = gameObject.GetComponents<Component>();
            var names = new List<string>();

            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                    continue;

                var name = component.GetType().Name;
                if (name == "Transform" || name == "RectTransform")
                    continue;

                names.Add(name);
            }

            return names.Count > 0 ? string.Join(", ", names) : string.Empty;
        }

        private static string BuildSelectionSummary(int maxItems)
        {
            maxItems = Mathf.Clamp(maxItems, 1, 20);
            var selection = Selection.objects;
            if (selection == null || selection.Length == 0)
                return "No active selection.";

            var sb = new StringBuilder();
            sb.AppendLine($"Selection ({Math.Min(selection.Length, maxItems)} item(s)):");

            for (int i = 0; i < selection.Length && i < maxItems; i++)
            {
                var obj = selection[i];
                if (obj == null)
                    continue;

                var assetPath = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(assetPath))
                    sb.AppendLine($"- {obj.name} [{obj.GetType().Name}]");
                else
                    sb.AppendLine($"- {obj.name} [{obj.GetType().Name}] -> {assetPath}");
            }

            return sb.ToString().Trim();
        }

        private string BuildConsoleErrorSummary(int count)
        {
            count = Mathf.Clamp(count, 1, 20);
            var cachedLogs = _unityLogsRepository?.GetRecentLogs("error", count, 0);
            if (!string.IsNullOrEmpty(cachedLogs))
                return cachedLogs.Trim();

            return "No recent console errors.";
        }

        private string BuildCompileErrorContext(int maxEntries, int snippetRadius)
        {
            if (_compilationService == null)
                return "Compilation service unavailable.";

            if (_compilationService.IsCompiling)
                return "Currently compiling... Please wait.";

            var compileSummary = _compilationService.GetCompilationErrors(maxEntries, false);
            if (string.IsNullOrEmpty(compileSummary) ||
                compileSummary.StartsWith("No compilation errors", StringComparison.OrdinalIgnoreCase))
            {
                return "No compilation errors detected.";
            }

            var sb = new StringBuilder();
            sb.AppendLine("Compilation error context:");
            sb.AppendLine(compileSummary.Trim());

            foreach (var snippet in ExtractFileSnippets(compileSummary, maxEntries, snippetRadius))
            {
                sb.AppendLine();
                sb.AppendLine(snippet);
            }

            return sb.ToString().Trim();
        }

        private IEnumerable<string> ExtractFileSnippets(string text, int maxEntries, int snippetRadius)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var regex = new Regex(@"\((?<path>[^:\n]+):(?<line>\d+)\)", RegexOptions.Multiline);
            var matches = regex.Matches(text ?? string.Empty);
            var count = 0;

            for (int i = 0; i < matches.Count && count < maxEntries; i++)
            {
                var match = matches[i];
                if (!match.Success)
                    continue;

                var rawPath = match.Groups["path"].Value.Trim();
                if (!seen.Add(rawPath))
                    continue;

                var fullPath = ResolveFilePath(rawPath);
                if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
                    continue;

                if (!int.TryParse(match.Groups["line"].Value, out var lineNumber))
                    lineNumber = 1;

                var lines = File.ReadAllLines(fullPath);
                var start = Math.Max(1, lineNumber - snippetRadius);
                var end = Math.Min(lines.Length, lineNumber + snippetRadius);
                var sb = new StringBuilder();
                sb.AppendLine($"[{rawPath}]");

                for (var line = start; line <= end; line++)
                    sb.AppendLine($"{line,4}: {lines[line - 1]}");

                yield return sb.ToString().TrimEnd();
                count++;
            }
        }

        private string ResolveFilePath(string rawPath)
        {
            if (Path.IsPathRooted(rawPath))
                return rawPath;

            if (_applicationPaths == null)
                return null;

            var normalized = rawPath.Replace("\\", "/");
            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(_applicationPaths.ProjectPath, normalized);

            return Path.Combine(_applicationPaths.ProjectPath, rawPath);
        }

        private static string TrimLines(string text, int maxLines)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length <= maxLines)
                return string.Join("\n", lines);

            var sb = new StringBuilder();
            for (int i = 0; i < maxLines; i++)
                sb.AppendLine(lines[i]);

            sb.Append("... (truncated)");
            return sb.ToString();
        }
    }
}
