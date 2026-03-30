// Copyright (C) GameBooom. Licensed under MIT.

using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace GameBooom.Editor.MCP
{
    [InitializeOnLoad]
    internal static class TempScriptRunner
    {
        private const string PendingKey = "GameBooom_PendingExecution";
        private const string ResultKey = "GameBooom_ExecutionResult";
        private const string TempDirectory = "Assets/unity-mcp/Editor/Temp";

        private static StringBuilder _compilationErrors;

        public static event Action<string> OnCompilationFailed;

        static TempScriptRunner()
        {
            var pending = SessionState.GetString(PendingKey, "");
            if (string.IsNullOrEmpty(pending))
                return;

            SessionState.EraseString(PendingKey);

            var separatorIndex = pending.IndexOf('|');
            if (separatorIndex < 0)
                return;

            var className = pending.Substring(0, separatorIndex);
            var tempPath = pending.Substring(separatorIndex + 1);

            EditorApplication.delayCall += () => RunPendingScript(className, tempPath);
        }

        public static void ScheduleExecution(string className, string tempPath)
        {
            SessionState.SetString(PendingKey, $"{className}|{tempPath}");
        }

        public static string ConsumeResult()
        {
            var result = SessionState.GetString(ResultKey, "");
            SessionState.EraseString(ResultKey);
            return result;
        }

        public static void CleanupAllTempScripts()
        {
            if (!Directory.Exists(TempDirectory))
                return;

            var files = Directory.GetFiles(TempDirectory, "GameBooom_TempScript_*.cs");
            if (files.Length == 0)
                return;

            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                    var metaPath = file + ".meta";
                    if (File.Exists(metaPath))
                        File.Delete(metaPath);
                }
                catch
                {
                }
            }

            Debug.Log($"[GameBooom] Cleaned up {files.Length} leftover temp scripts.");
            AssetDatabase.Refresh();
        }

        public static void RegisterCompilationCheck()
        {
            _compilationErrors = new StringBuilder();
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished += OnAllCompilationFinished;
        }

        private static void RunPendingScript(string className, string tempPath)
        {
            string result;
            try
            {
                var type = FindType(className);
                if (type == null)
                {
                    result = $"Error: Type '{className}' not found after compilation. Check console for compile errors.";
                    StoreResult(result);
                    CleanupFile(tempPath);
                    return;
                }

                var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                {
                    result = $"Error: static Run() method not found in '{className}'.";
                    StoreResult(result);
                    CleanupFile(tempPath);
                    return;
                }

                var returnValue = method.Invoke(null, null);
                result = returnValue?.ToString() ?? "OK (no return value)";
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException ?? ex;
                result = $"Error: {inner.Message}\n{inner.StackTrace}";
                Debug.LogError($"[GameBooom] Script execution failed: {inner.Message}\n{inner.StackTrace}");
            }
            catch (Exception ex)
            {
                result = $"Error: {ex.Message}";
                Debug.LogError($"[GameBooom] Script execution failed: {ex.Message}");
            }

            StoreResult(result);
            CleanupFile(tempPath);
        }

        private static Type FindType(string className)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                    continue;

                try
                {
                    var type = assembly.GetType(className);
                    if (type != null)
                        return type;
                }
                catch
                {
                }
            }

            return null;
        }

        private static void CleanupFile(string tempPath)
        {
            try
            {
                if (!File.Exists(tempPath))
                    return;

                File.Delete(tempPath);
                var metaPath = tempPath + ".meta";
                if (File.Exists(metaPath))
                    File.Delete(metaPath);

                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GameBooom] Failed to clean up temp script: {ex.Message}");
            }
        }

        private static void StoreResult(string result)
        {
            SessionState.SetString(ResultKey, result);
            Debug.Log($"[GameBooom] Script execution result: {result}");
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            foreach (var message in messages)
            {
                if (message.type == CompilerMessageType.Error)
                    _compilationErrors.AppendLine($"  {message.file}({message.line}): {message.message}");
            }
        }

        private static void OnAllCompilationFinished(object context)
        {
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished -= OnAllCompilationFinished;

            EditorApplication.delayCall += HandleCompilationFailure;
        }

        private static void HandleCompilationFailure()
        {
            var pending = SessionState.GetString(PendingKey, "");
            if (string.IsNullOrEmpty(pending))
                return;

            SessionState.EraseString(PendingKey);

            var separatorIndex = pending.IndexOf('|');
            if (separatorIndex < 0)
                return;

            var tempPath = pending.Substring(separatorIndex + 1);
            var errorText = _compilationErrors?.ToString() ?? "";
            var result = string.IsNullOrWhiteSpace(errorText)
                ? "Compilation failed. Check Unity console for errors."
                : $"Compilation failed with errors:\n{errorText}";

            StoreResult(result);
            CleanupFile(tempPath);
            OnCompilationFailed?.Invoke(result);
            Debug.LogWarning("[GameBooom] Generated script had compilation errors. Cleaned up temp file.");
        }
    }
}
