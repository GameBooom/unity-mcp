// Copyright (C) Funplay. Licensed under MIT.

using System;
using System.Collections.Generic;
using Funplay.Editor.MCP.Server;
using Funplay.Editor.Tools;
using UnityEditor;
using UnityEngine;

namespace Funplay.Editor.State
{
    /// <summary>
    /// Saves and restores running state across Unity domain reloads (triggered by script recompilation).
    /// Uses SessionState (persists within editor session, cleared on editor restart).
    /// </summary>
    internal static class DomainReloadHandler
    {
        private const string StateKey = "Funplay_ReloadState";
        private const string TimestampKey = "Funplay_ReloadTimestamp";
        private const string PendingFunctionKey = "Funplay_ReloadPendingFunction";
        private const string LastRecoveryInfoKey = "Funplay_LastRecoveryInfo";
        private const string ResumeCountKey = "Funplay_ConsecutiveResumeCount";
        private const string LastResumeTimestampKey = "Funplay_LastResumeTimestamp";

        private const int MaxConsecutiveResumes = 5;
        private const double ResumeCountResetSeconds = 120;
        private const double DeferredCompletionTimeoutSeconds = 15;

        private static bool _registered;
        private static bool _deferredCompletionRegistered;
        private static DateTime _deferredCompletionStartedAt;
        private static IStateController _deferredCompletionStateController;

        /// <summary>
        /// Register to receive reload events. Call once (idempotent).
        /// </summary>
        public static void Register(IStateController stateController)
        {
            if (_registered) return;
            _registered = true;

            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                SaveState(stateController.CurrentState);
            };
        }

        public static void SaveState(FunplayState state)
        {
            SessionState.SetString(StateKey, state.ToString());
            SessionState.SetString(TimestampKey, DateTime.Now.ToString("O"));
        }

        public static void SavePendingFunction(FunctionCall functionCall)
        {
            if (functionCall == null)
                return;

            var payload = new Dictionary<string, object>
            {
                ["id"] = functionCall.Id ?? string.Empty,
                ["toolCallId"] = functionCall.ToolCallId ?? string.Empty,
                ["functionName"] = functionCall.FunctionName ?? string.Empty,
                ["rawArguments"] = functionCall.RawArguments ?? string.Empty,
                ["createdAt"] = functionCall.CreatedAt.ToString("O"),
                ["parameters"] = functionCall.Parameters ?? new Dictionary<string, string>()
            };

            SessionState.SetString(PendingFunctionKey, SimpleJsonHelper.Serialize(payload));
        }

        public static void ClearPendingFunction()
        {
            SessionState.EraseString(PendingFunctionKey);
        }

        public static void CompletePendingFunction(IStateController stateController)
        {
            if (ShouldDeferPendingCompletion())
            {
                DeferPendingCompletion(stateController);
                return;
            }

            ClearPendingFunction();
            stateController?.ReturnToPreviousState();
        }

        public static void StoreRecoveryInfo(string toolName, string status, string summary)
        {
            var payload = new Dictionary<string, object>
            {
                ["toolName"] = toolName ?? string.Empty,
                ["status"] = status ?? string.Empty,
                ["summary"] = summary ?? string.Empty,
                ["timestamp"] = DateTime.Now.ToString("O")
            };

            SessionState.SetString(LastRecoveryInfoKey, SimpleJsonHelper.Serialize(payload));
        }

        public static RecoveryInfo GetLastRecoveryInfo(bool consume = false)
        {
            var infoStr = SessionState.GetString(LastRecoveryInfoKey, "");
            if (consume)
                SessionState.EraseString(LastRecoveryInfoKey);

            if (string.IsNullOrEmpty(infoStr))
                return null;

            try
            {
                var dict = SimpleJsonHelper.Deserialize(infoStr) as Dictionary<string, object>;
                if (dict == null)
                    return null;

                var result = new RecoveryInfo
                {
                    ToolName = GetString(dict, "toolName"),
                    Status = GetString(dict, "status"),
                    Summary = GetString(dict, "summary")
                };

                var timestampStr = GetString(dict, "timestamp");
                if (DateTime.TryParse(timestampStr, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var timestamp))
                {
                    result.Timestamp = timestamp;
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Funplay] Failed to parse recovery info: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Checks whether auto-resume is allowed based on the consecutive resume counter.
        /// </summary>
        public static bool CanAutoResume()
        {
            var count = SessionState.GetInt(ResumeCountKey, 0);
            var lastTimestampStr = SessionState.GetString(LastResumeTimestampKey, "");

            if (DateTime.TryParse(lastTimestampStr, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var lastTs))
            {
                if ((DateTime.Now - lastTs).TotalSeconds > ResumeCountResetSeconds)
                    count = 0;
            }

            return count < MaxConsecutiveResumes;
        }

        public static void RecordAutoResume()
        {
            var count = SessionState.GetInt(ResumeCountKey, 0);
            var lastTimestampStr = SessionState.GetString(LastResumeTimestampKey, "");

            if (DateTime.TryParse(lastTimestampStr, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var lastTs))
            {
                if ((DateTime.Now - lastTs).TotalSeconds > ResumeCountResetSeconds)
                    count = 0;
            }

            count++;
            SessionState.SetInt(ResumeCountKey, count);
            SessionState.SetString(LastResumeTimestampKey, DateTime.Now.ToString("O"));
        }

        public static void ResetResumeCounter()
        {
            SessionState.SetInt(ResumeCountKey, 0);
            SessionState.EraseString(LastResumeTimestampKey);
        }

        /// <summary>
        /// Checks if there was an interrupted operation before the last domain reload.
        /// Returns the state that was active, or null if nothing was running.
        /// Clears the saved state after reading (one-shot).
        /// </summary>
        public static InterruptedState ConsumeInterruptedState()
        {
            var stateStr = SessionState.GetString(StateKey, "");
            var timestampStr = SessionState.GetString(TimestampKey, "");
            var pendingFunctionStr = SessionState.GetString(PendingFunctionKey, "");
            var pendingFunction = ParsePendingFunction(pendingFunctionStr);

            // Clear after reading
            SessionState.EraseString(StateKey);
            SessionState.EraseString(TimestampKey);
            SessionState.EraseString(PendingFunctionKey);

            if (string.IsNullOrEmpty(stateStr) && pendingFunction == null) return null;

            if (!Enum.TryParse<FunplayState>(stateStr, out var state))
                state = pendingFunction != null ? FunplayState.ExecutingFunction : FunplayState.Initialized;

            if (state == FunplayState.Initialized && pendingFunction == null)
                return null;

            // Discard if too old (> 120 seconds)
            if (DateTime.TryParse(timestampStr, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var ts))
            {
                if ((DateTime.Now - ts).TotalSeconds > 120)
                    return null;
            }

            return new InterruptedState
            {
                State = state,
                PendingFunction = pendingFunction,
                Timestamp = ts
            };
        }

        private static bool ShouldDeferPendingCompletion()
        {
            return EditorApplication.isPlayingOrWillChangePlaymode ||
                   EditorApplication.isCompiling ||
                   EditorApplication.isUpdating;
        }

        private static void DeferPendingCompletion(IStateController stateController)
        {
            _deferredCompletionStateController = stateController;
            _deferredCompletionStartedAt = DateTime.Now;

            if (_deferredCompletionRegistered)
                return;

            _deferredCompletionRegistered = true;
            EditorApplication.update += CompletePendingWhenEditorIsStable;
        }

        private static void CompletePendingWhenEditorIsStable()
        {
            if (ShouldDeferPendingCompletion() &&
                (DateTime.Now - _deferredCompletionStartedAt).TotalSeconds < DeferredCompletionTimeoutSeconds)
            {
                return;
            }

            EditorApplication.update -= CompletePendingWhenEditorIsStable;
            _deferredCompletionRegistered = false;

            ClearPendingFunction();
            _deferredCompletionStateController?.ReturnToPreviousState();
            _deferredCompletionStateController = null;
        }

        private static PendingFunctionInfo ParsePendingFunction(string pendingFunctionStr)
        {
            if (string.IsNullOrEmpty(pendingFunctionStr))
                return null;

            try
            {
                var dict = SimpleJsonHelper.Deserialize(pendingFunctionStr) as Dictionary<string, object>;
                if (dict == null)
                    return null;

                var result = new PendingFunctionInfo
                {
                    Id = GetString(dict, "id"),
                    ToolCallId = GetString(dict, "toolCallId"),
                    FunctionName = GetString(dict, "functionName"),
                    RawArguments = GetString(dict, "rawArguments")
                };

                var createdAtStr = GetString(dict, "createdAt");
                if (DateTime.TryParse(createdAtStr, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var createdAt))
                {
                    result.CreatedAt = createdAt;
                }

                if (dict.TryGetValue("parameters", out var parametersObj) &&
                    parametersObj is Dictionary<string, object> rawParameters)
                {
                    foreach (var entry in rawParameters)
                    {
                        result.Parameters[entry.Key] = entry.Value?.ToString() ?? string.Empty;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Funplay] Failed to parse pending function state: {ex.Message}");
                return null;
            }
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            return dict.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;
        }

        internal class InterruptedState
        {
            public FunplayState State;
            public DateTime Timestamp;
            public PendingFunctionInfo PendingFunction;

            public string GetDescription()
            {
                if (!string.IsNullOrEmpty(PendingFunction?.FunctionName))
                    return $"Tool '{PendingFunction.FunctionName}' was interrupted by script recompilation.";

                switch (State)
                {
                    case FunplayState.ExecutingFunction:
                    case FunplayState.ExecutingAllFunctions:
                        return "Function execution was interrupted by script recompilation.";
                    default:
                        return "Operation was interrupted by script recompilation.";
                }
            }
        }

        internal class PendingFunctionInfo
        {
            public string Id;
            public string ToolCallId;
            public string FunctionName;
            public string RawArguments;
            public DateTime CreatedAt;
            public Dictionary<string, string> Parameters = new Dictionary<string, string>();
        }

        internal class RecoveryInfo
        {
            public string ToolName;
            public string Status;
            public string Summary;
            public DateTime Timestamp;
        }
    }
}
