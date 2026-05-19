// Copyright (C) Funplay. Licensed under MIT.
using System;
using System.Threading.Tasks;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using Funplay.Editor.DI;
using Funplay.Editor.MCP.Server;
using Funplay.Editor.Services;
using Funplay.Editor.State;
using UnityEditor;
using UnityEngine;

namespace Funplay.Editor.Tools.Builtins
{
    [ToolProvider("Compilation")]
    internal static class CompilationFunctions
    {
        private const string ExternalSyncPendingKey = "Funplay_ExternalSync_Pending";
        private const string ExternalSyncStartedAtKey = "Funplay_ExternalSync_StartedAt";
        private const double ExternalSyncRecoveryMaxAgeSeconds = 120;

        [Description("Force Unity to refresh and wait until script compilation is complete without blocking the editor thread. " +
                     "Use this after editing scripts to ensure the latest code is active before entering Play Mode. " +
                     "Returns compilation errors if any, or a success message.")]
        [ReadOnlyTool]
        public static async Task<string> WaitForCompilation(
            [ToolParam("Force a reimport/refresh before waiting", Required = false)] bool force_refresh = true,
            [ToolParam("Maximum seconds to wait for compilation", Required = false)] int timeout_seconds = 30)
        {
            try
            {
                timeout_seconds = Mathf.Clamp(timeout_seconds, 5, 120);

                var compilationService = GetCompilationService();
                if (compilationService == null)
                    return "Error: Compilation service is unavailable";

                var startTime = DateTime.UtcNow;
                bool completed = await compilationService
                    .WaitForCompilationAsync(force_refresh, timeout_seconds)
                    .ConfigureAwait(false);

                if (!completed)
                    return $"Error: Compilation timed out after {timeout_seconds}s (still compiling)";

                var issues = compilationService.GetCompilationErrors();
                if (!string.Equals(issues, "No compilation errors detected.", StringComparison.Ordinal))
                    return issues;

                double elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                return $"Compilation complete ({elapsed:F1}s). No errors detected.";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        [Description("IMPORTANT: This should be the AI's default next step immediately after editing project files outside the Unity Editor. " +
                     "Treat this as required after any external code or asset change. " +
                     "Call it after modifying .cs, .asmdef, .shader, prefabs, scenes, ScriptableObjects, or other Assets files, and before running tests, entering Play Mode, executing follow-up tools, or assuming Unity has imported the latest state. " +
                     "It forces Unity to import external file changes and handles any resulting script compilation or domain reload recovery.")]
        [ReadOnlyTool]
        public static async Task<string> RequestRecompile(
            [ToolParam("Maximum seconds to wait for compilation", Required = false)] int timeout_seconds = 30)
        {
            if (EditorApplication.isPlaying)
            {
                return "Error: Unity is in Play Mode. Unity does not process script recompilation " +
                       "or domain reloads while playing. Call exit_play_mode first, then retry request_recompile.";
            }

            MarkExternalSyncPending();
            timeout_seconds = Mathf.Clamp(timeout_seconds, 5, 120);

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            if (EditorApplication.isCompiling)
            {
                ExternalSyncRecoveryTracker.TryCompletePendingRecovery();
                return "External changes imported. Unity started recompiling scripts and may reload the domain. " +
                       "If this request is interrupted, call get_reload_recovery_status for the final outcome.";
            }

            var compilationService = GetCompilationService();
            if (compilationService == null)
            {
                ClearExternalSyncPending();
                return "External changes imported. Compilation service is unavailable, so compilation status could not be checked.";
            }

            bool completed = await compilationService
                .WaitForCompilationAsync(forceRefresh: false, timeoutSeconds: timeout_seconds)
                .ConfigureAwait(false);

            if (!completed)
            {
                ExternalSyncRecoveryTracker.TryCompletePendingRecovery();
                return "External changes imported. Unity is still compiling in the background. " +
                       "Call get_reload_recovery_status or get_compilation_errors after it finishes.";
            }

            var issues = compilationService.GetCompilationErrors(includeWarnings: true);
            ClearExternalSyncPending();

            if (!string.Equals(issues, "No compilation errors or warnings detected.", StringComparison.Ordinal) &&
                !string.Equals(issues, "No compilation errors detected.", StringComparison.Ordinal))
            {
                return issues;
            }

            return "External changes imported. No compilation errors or warnings detected.";
        }

        private static string RequestScriptCompilationOnly()
        {
            try
            {
                UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
                return "Script recompilation requested. Call get_compilation_errors or wait_for_compilation after it finishes.";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        [Description("Get the latest Unity script compilation errors from the most recent compilation cycle.")]
        [ReadOnlyTool]
        public static string GetCompilationErrors(
            [ToolParam("Maximum number of issues to return", Required = false)] int max_entries = 50,
            [ToolParam("Include warnings in addition to errors", Required = false)] bool include_warnings = false)
        {
            var compilationService = GetCompilationService();
            if (compilationService == null)
                return "Error: Compilation service is unavailable";

            if (compilationService.IsCompiling)
                return "Currently compiling... Please wait and try again.";

            return compilationService.GetCompilationErrors(max_entries, include_warnings);
        }

        [Description("Get the latest domain reload recovery event, if any. Useful after Unity recompiles scripts and an MCP request gets interrupted.")]
        [ReadOnlyTool]
        public static string GetReloadRecoveryStatus(
            [ToolParam("Consume and clear the stored recovery event after reading", Required = false)] bool consume = false)
        {
            var info = DomainReloadHandler.GetLastRecoveryInfo(consume);
            if (info == null)
                return "No reload recovery event recorded.";

            return $"Recovery event:\n" +
                   $"- Tool: {info.ToolName}\n" +
                   $"- Status: {info.Status}\n" +
                   $"- Time: {info.Timestamp:O}\n" +
                   $"- Summary: {info.Summary}";
        }

        private static ICompilationService GetCompilationService()
        {
            return RootScopeServices.Services?.GetService(typeof(ICompilationService)) as ICompilationService
                   ?? CompilationService.Instance;
        }

        internal static void MarkExternalSyncPending()
        {
            SessionState.SetBool(ExternalSyncPendingKey, true);
            SessionState.SetString(ExternalSyncStartedAtKey, DateTime.Now.ToString("O"));
        }

        internal static void ClearExternalSyncPending()
        {
            SessionState.EraseBool(ExternalSyncPendingKey);
            SessionState.EraseString(ExternalSyncStartedAtKey);
        }

        internal static bool HasPendingExternalSync()
        {
            if (!SessionState.GetBool(ExternalSyncPendingKey, false))
                return false;

            var startedAtStr = SessionState.GetString(ExternalSyncStartedAtKey, "");
            if (!DateTime.TryParse(startedAtStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var startedAt))
                return true;

            if ((DateTime.Now - startedAt).TotalSeconds <= ExternalSyncRecoveryMaxAgeSeconds)
                return true;

            ClearExternalSyncPending();
            return false;
        }
    }

    [InitializeOnLoad]
    internal static class ExternalSyncRecoveryTracker
    {
        static ExternalSyncRecoveryTracker()
        {
            TryCompletePendingRecovery();
        }

        internal static void TryCompletePendingRecovery()
        {
            if (!CompilationFunctions.HasPendingExternalSync())
                return;

            if (!EditorApplication.isCompiling)
            {
                CompleteRecovery();
                return;
            }

            EditorApplication.update += WaitUntilCompilationEnds;
        }

        private static void WaitUntilCompilationEnds()
        {
            if (EditorApplication.isCompiling)
                return;

            EditorApplication.update -= WaitUntilCompilationEnds;
            CompleteRecovery();
        }

        private static void CompleteRecovery()
        {
            if (!CompilationFunctions.HasPendingExternalSync())
                return;

            CompilationFunctions.ClearExternalSyncPending();

            var compilationService = RootScopeServices.Services?.GetService(typeof(ICompilationService)) as ICompilationService
                                     ?? CompilationService.Instance;
            if (compilationService == null)
            {
                DomainReloadHandler.StoreRecoveryInfo(
                    "request_recompile",
                    MCPToolCallStatus.Error.ToString(),
                    "External changes were imported, but compilation service was unavailable after domain reload.");
                return;
            }

            var issues = compilationService.GetCompilationErrors(includeWarnings: true);
            var hasIssues = !string.Equals(issues, "No compilation errors or warnings detected.", StringComparison.Ordinal) &&
                            !string.Equals(issues, "No compilation errors detected.", StringComparison.Ordinal);

            if (hasIssues)
            {
                DomainReloadHandler.StoreRecoveryInfo(
                    "request_recompile",
                    MCPToolCallStatus.Error.ToString(),
                    "External changes were imported, but compilation reported issues.\n" + issues);
                return;
            }

            DomainReloadHandler.StoreRecoveryInfo(
                "request_recompile",
                MCPToolCallStatus.Success.ToString(),
                "External changes were imported and script compilation finished successfully after domain reload.");
        }
    }
}
