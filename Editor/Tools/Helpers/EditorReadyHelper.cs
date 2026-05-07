// Copyright (C) Funplay. Licensed under MIT.

using System;
using System.Threading.Tasks;
using UnityEditor;

namespace Funplay.Editor.Tools.Helpers
{
    /// <summary>
    /// Wait for the editor to finish compiling and importing before running a tool.
    /// Tools that depend on the latest assemblies / asset state — most notably
    /// <c>execute_code</c> after an external file edit — should call
    /// <see cref="RefreshAndWaitForReady"/> first so agents don't have to chain
    /// <c>request_recompile</c> manually every time.
    /// </summary>
    internal static class EditorReadyHelper
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

        public static async Task RefreshAndWaitForReady()
        {
            AssetDatabase.Refresh();

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                await WaitForEditorReadyAsync(DefaultTimeout);
        }

        public static Task WaitForEditorReadyAsync(TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var start = DateTime.UtcNow;

            void Tick()
            {
                if (tcs.Task.IsCompleted)
                {
                    EditorApplication.update -= Tick;
                    return;
                }

                if ((DateTime.UtcNow - start) > timeout)
                {
                    EditorApplication.update -= Tick;
                    tcs.TrySetException(new TimeoutException("Editor still busy after timeout"));
                    return;
                }

                if (!EditorApplication.isCompiling && !EditorApplication.isUpdating)
                {
                    EditorApplication.update -= Tick;
                    tcs.TrySetResult(true);
                }
            }

            EditorApplication.update += Tick;
            EditorApplication.QueuePlayerLoopUpdate();
            return tcs.Task;
        }
    }
}
