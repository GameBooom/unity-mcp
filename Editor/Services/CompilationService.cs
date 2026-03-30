// Copyright (C) GameBooom. Licensed under MIT.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;

namespace GameBooom.Editor.Services
{
    [InitializeOnLoad]
    internal class CompilationService : ICompilationService, IDisposable
    {
        private static readonly object SyncRoot = new object();
        private static readonly List<CompilerMessage> LatestMessages = new List<CompilerMessage>();
        private static TaskCompletionSource<bool> _compilationFinishedTcs = CreateCompletionSource();
        private static event Action CompilationFinished;
        private static bool _subscribed;

        public static CompilationService Instance { get; private set; }

        public bool IsCompiling => EditorApplication.isCompiling;
        public event Action OnCompilationFinished
        {
            add => CompilationFinished += value;
            remove => CompilationFinished -= value;
        }

        static CompilationService()
        {
            EnsureInitialized();
            Instance = new CompilationService();
        }

        public CompilationService()
        {
            EnsureInitialized();
            Instance = this;
        }

        public async Task<bool> WaitForCompilationAsync(bool forceRefresh, int timeoutSeconds)
        {
            if (forceRefresh)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                if (!await WaitForCompilationToStartAsync(timeoutSeconds).ConfigureAwait(false))
                    return true;
            }
            else if (!EditorApplication.isCompiling)
            {
                return true;
            }

            TaskCompletionSource<bool> waitSource;
            lock (SyncRoot)
            {
                if (_compilationFinishedTcs == null || _compilationFinishedTcs.Task.IsCompleted)
                {
                    _compilationFinishedTcs = CreateCompletionSource();
                }

                waitSource = _compilationFinishedTcs;
            }

            var completedTask = await Task.WhenAny(
                waitSource.Task,
                Task.Delay(TimeSpan.FromSeconds(timeoutSeconds))).ConfigureAwait(false);

            return completedTask == waitSource.Task && waitSource.Task.IsCompletedSuccessfully;
        }

        public string GetCompilationErrors(int maxEntries = 50, bool includeWarnings = false)
        {
            maxEntries = Math.Max(1, maxEntries);

            List<CompilerMessage> messages;
            lock (SyncRoot)
            {
                messages = LatestMessages.ToList();
            }

            var filtered = messages
                .Where(message => message.type == CompilerMessageType.Error ||
                                  (includeWarnings && message.type == CompilerMessageType.Warning))
                .Take(maxEntries)
                .ToList();

            if (filtered.Count == 0)
            {
                return includeWarnings
                    ? "No compilation errors or warnings detected."
                    : "No compilation errors detected.";
            }

            var lines = filtered.Select(message =>
            {
                var location = string.IsNullOrEmpty(message.file)
                    ? string.Empty
                    : $" ({message.file}:{message.line})";
                return $"- [{message.type}] {message.message}{location}";
            });

            return "Compilation issues:\n" + string.Join("\n", lines);
        }

        private static void EnsureInitialized()
        {
            if (_subscribed)
                return;

            _subscribed = true;
            CompilationPipeline.compilationStarted += HandleCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += HandleAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished += HandleCompilationFinished;
        }

        private static async Task<bool> WaitForCompilationToStartAsync(int timeoutSeconds)
        {
            if (EditorApplication.isCompiling)
                return true;

            var waitUntil = DateTime.UtcNow.AddSeconds(Math.Min(timeoutSeconds, 2));
            while (DateTime.UtcNow < waitUntil)
            {
                if (EditorApplication.isCompiling)
                    return true;

                await Task.Delay(100).ConfigureAwait(false);
            }

            return false;
        }

        private static void HandleCompilationStarted(object context)
        {
            lock (SyncRoot)
            {
                LatestMessages.Clear();
                _compilationFinishedTcs = CreateCompletionSource();
            }
        }

        private static void HandleAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (messages == null || messages.Length == 0)
            {
                return;
            }

            lock (SyncRoot)
            {
                LatestMessages.AddRange(messages);
            }
        }

        private static void HandleCompilationFinished(object obj)
        {
            TaskCompletionSource<bool> waitSource = null;
            lock (SyncRoot)
            {
                waitSource = _compilationFinishedTcs;
            }

            waitSource?.TrySetResult(true);
            CompilationFinished?.Invoke();
        }

        private static TaskCompletionSource<bool> CreateCompletionSource()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void Dispose()
        {
        }
    }
}
