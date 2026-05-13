// Copyright (C) Funplay. Licensed under MIT.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Funplay.Editor.Tools.Helpers;
using Funplay.Editor.Tools.Scripting;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Funplay.Editor.Tools.Builtins
{
    [ToolProvider("Script")]
    internal static class ScriptExecutionFunctions
    {
        private static Type _providerType;
        private static Type _paramsType;
        private static bool _typesResolved;
        private static string _typeLoadError;

        private const string HistorySessionKey = "Funplay.MCP.ExecuteCode.History";
        private const int HistoryMaxEntries = 50;

        // Patterns blocked when safety_checks=true. Conservative starter set — covers the
        // most common "AI accidentally destroyed something" cases without locking down
        // legitimate Editor automation (we deliberately keep System.Net / reflection allowed).
        private static readonly (string Pattern, string Reason)[] SafetyBlocklist = new[]
        {
            (@"\bFile\.Delete\b",                  "File.Delete blocked by safety_checks"),
            (@"\bDirectory\.Delete\b",             "Directory.Delete blocked by safety_checks"),
            (@"\bSystem\.IO\.File\.Delete\b",      "System.IO.File.Delete blocked by safety_checks"),
            (@"\bProcess\.Start\b",                "Process.Start blocked by safety_checks"),
            (@"\bSystem\.Diagnostics\.Process\b",  "System.Diagnostics.Process blocked by safety_checks"),
            (@"\bEnvironment\.Exit\b",             "Environment.Exit blocked by safety_checks"),
            (@"\bApplication\.Quit\b",             "Application.Quit blocked by safety_checks"),
            (@"\bAssetDatabase\.DeleteAsset\b",    "AssetDatabase.DeleteAsset blocked by safety_checks"),
            (@"\bwhile\s*\(\s*true\s*\)",          "Infinite while(true) loop blocked by safety_checks"),
            (@"\bfor\s*\(\s*;\s*;\s*\)",           "Infinite for(;;) loop blocked by safety_checks"),
        };

        [Description("Primary high-flexibility execution tool. Compiles a C# snippet in memory and runs it on the editor thread. " +
                     "Two templates are supported:\n" +
                     "  1) Recommended: implement IFunplayCommand on a class — receives an ExecutionContext (ctx) " +
                     "with RegisterObjectCreation/RegisterObjectModification/DestroyObject (auto-Undo + tracked) and " +
                     "Log/LogWarning/LogError (returned in the response).\n" +
                     "  2) Legacy: any class with `public static string Run()` — return value becomes the response message.\n" +
                     "Before compiling, the editor's AssetDatabase is refreshed and pending compilation is awaited, " +
                     "so external file edits are picked up automatically without a separate request_recompile. " +
                     "Set safety_checks=true to block a small set of obviously dangerous patterns " +
                     "(File.Delete, Process.Start, while(true), Environment.Exit, AssetDatabase.DeleteAsset, etc) " +
                     "as a defensive layer; default is false because Funplay relies on client-side approval. " +
                     "Every invocation is appended to a session-scoped history (see get_execute_code_history / replay_execute_code).")]
        [SceneEditingTool]
        public static async Task<object> ExecuteCode(
            [ToolParam("C# code to execute. See description for IFunplayCommand vs legacy Run() templates.")] string code,
            [ToolParam("If true, reject the call before compile when the code contains obviously dangerous patterns. Default false.", Required = false)] bool safety_checks = false)
        {
            if (safety_checks)
            {
                foreach (var (pattern, reason) in SafetyBlocklist)
                {
                    if (Regex.IsMatch(code, pattern))
                    {
                        var blocked = Response.Error("SAFETY_CHECK_BLOCKED",
                            new { pattern, reason, hint = "Pass safety_checks=false to bypass." });
                        AppendHistory(code, false, $"Blocked: {reason}");
                        return blocked;
                    }
                }
            }

            try
            {
                await EditorReadyHelper.RefreshAndWaitForReady();
            }
            catch (TimeoutException)
            {
                AppendHistory(code, false, "EDITOR_BUSY");
                return Response.Error("EDITOR_BUSY",
                    new { hint = "Unity is still compiling/importing. Retry in a moment." });
            }

            var className = "TempScript_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var actualClassName = className;
            var projectUsings = GetProjectNamespaceUsings();

            string fullCode;
            if (code.Contains("class "))
            {
                var match = Regex.Match(code, @"class\s+(\w+)");
                if (match.Success)
                    actualClassName = match.Groups[1].Value;

                fullCode = PrependMissingUsings(code, projectUsings);
            }
            else
            {
                fullCode = WrapCode(code, className, projectUsings);
            }

            try
            {
                var result = CompileAndExecute(fullCode, actualClassName);
                AppendHistory(code, IsSuccess(result), SummarizeResult(result));
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Funplay] ExecuteCode failed: {ex.Message}");
                AppendHistory(code, false, ex.Message);
                return Response.Error($"EXECUTE_CODE_FAILED: {ex.Message}");
            }
        }

        [Description("Return the most recent execute_code invocations (success or failure) from the current Editor session. " +
                     "Use replay_execute_code to re-run a past entry by index. History survives domain reloads but is cleared " +
                     "when the Editor closes (uses SessionState). Cap is 50 entries; older entries are dropped.")]
        [ReadOnlyTool]
        public static object GetExecuteCodeHistory(
            [ToolParam("Number of most-recent entries to return (1-50). Default 10.", Required = false)] int limit = 10)
        {
            var entries = LoadHistory().entries;
            limit = Mathf.Clamp(limit, 1, HistoryMaxEntries);
            var slice = entries.Skip(Math.Max(0, entries.Count - limit)).ToList();
            // Render newest first, with original index preserved (for replay)
            var view = new List<object>(slice.Count);
            for (int i = slice.Count - 1; i >= 0; i--)
            {
                var entry = slice[i];
                var globalIndex = entries.IndexOf(entry);
                view.Add(new
                {
                    index = globalIndex,
                    timestamp = entry.timestamp,
                    success = entry.success,
                    summary = entry.summary,
                    code_preview = Preview(entry.code, 240),
                    code_length = entry.code?.Length ?? 0
                });
            }
            return Response.Success($"Returned {view.Count} of {entries.Count} history entries.",
                new { total = entries.Count, returned = view.Count, entries = view });
        }

        [Description("Re-run a past execute_code invocation by index (use get_execute_code_history to discover indices). " +
                     "The original code is re-compiled and executed; this also appends a new history entry. " +
                     "Pass safety_checks if you want the rerun to be gated, default false.")]
        [SceneEditingTool]
        public static async Task<object> ReplayExecuteCode(
            [ToolParam("History index to replay (as returned by get_execute_code_history).")] int index,
            [ToolParam("If true, re-evaluate the safety blocklist before re-running. Default false.", Required = false)] bool safety_checks = false)
        {
            var entries = LoadHistory().entries;
            if (entries.Count == 0)
                return Response.Error("HISTORY_EMPTY");
            if (index < 0 || index >= entries.Count)
                return Response.Error("HISTORY_INDEX_OUT_OF_RANGE",
                    new { provided = index, valid_range = new { min = 0, max = entries.Count - 1 } });

            var entry = entries[index];
            if (string.IsNullOrEmpty(entry.code))
                return Response.Error("HISTORY_ENTRY_EMPTY", new { index });

            return await ExecuteCode(entry.code, safety_checks);
        }

        [Description("Erase the entire execute_code history for the current Editor session. " +
                     "Useful when you want a clean slate before a fresh experiment or before sharing a session recording.")]
        [SceneEditingTool]
        public static object ClearExecuteCodeHistory()
        {
            var before = LoadHistory().entries.Count;
            SessionState.EraseString(HistorySessionKey);
            return Response.Success($"Cleared {before} history entr{(before == 1 ? "y" : "ies")}.",
                new { cleared = before });
        }

        // ---- History helpers ----------------------------------------------------

        [Serializable]
        private class HistoryEntry
        {
            public string timestamp;
            public string code;
            public bool success;
            public string summary;
        }

        [Serializable]
        private class HistoryBox
        {
            public List<HistoryEntry> entries = new List<HistoryEntry>();
        }

        private static HistoryBox LoadHistory()
        {
            var raw = SessionState.GetString(HistorySessionKey, null);
            if (string.IsNullOrEmpty(raw))
                return new HistoryBox();
            try
            {
                return JsonConvert.DeserializeObject<HistoryBox>(raw) ?? new HistoryBox();
            }
            catch
            {
                return new HistoryBox();
            }
        }

        private static void AppendHistory(string code, bool success, string summary)
        {
            try
            {
                var box = LoadHistory();
                box.entries.Add(new HistoryEntry
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    code = code ?? string.Empty,
                    success = success,
                    summary = Preview(summary, 200)
                });
                if (box.entries.Count > HistoryMaxEntries)
                    box.entries.RemoveRange(0, box.entries.Count - HistoryMaxEntries);
                SessionState.SetString(HistorySessionKey, JsonConvert.SerializeObject(box));
            }
            catch (Exception ex)
            {
                // Swallow — history is best-effort, must never break real execution.
                Debug.LogWarning($"[Funplay] Failed to append execute_code history: {ex.Message}");
            }
        }

        private static bool IsSuccess(object result)
        {
            if (result == null) return false;
            var prop = result.GetType().GetProperty("success");
            return prop?.GetValue(result) is bool b && b;
        }

        private static string SummarizeResult(object result)
        {
            if (result == null) return "null";
            var t = result.GetType();
            var success = t.GetProperty("success")?.GetValue(result) as bool? ?? false;
            if (success)
                return (t.GetProperty("message")?.GetValue(result) as string) ?? "OK";
            var code = t.GetProperty("code")?.GetValue(result) as string;
            var error = t.GetProperty("error")?.GetValue(result) as string;
            return code ?? error ?? "ERROR";
        }

        private static string Preview(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? string.Empty;
            return s.Substring(0, max) + "…";
        }

        private static bool EnsureCodeDomTypes()
        {
            if (_typesResolved)
                return _providerType != null;

            _typesResolved = true;

            try
            {
                _providerType = Type.GetType("Microsoft.CSharp.CSharpCodeProvider, System");
                _paramsType = Type.GetType("System.CodeDom.Compiler.CompilerParameters, System");

                if (_providerType == null || _paramsType == null)
                {
                    try
                    {
                        var codeDomAssembly = Assembly.Load("System.CodeDom");
                        _providerType = _providerType ?? codeDomAssembly.GetType("Microsoft.CSharp.CSharpCodeProvider");
                        _paramsType = _paramsType ?? codeDomAssembly.GetType("System.CodeDom.Compiler.CompilerParameters");
                    }
                    catch
                    {
                    }
                }

                if (_providerType == null || _paramsType == null)
                {
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (assembly.IsDynamic)
                            continue;

                        try
                        {
                            _providerType = _providerType ?? assembly.GetType("Microsoft.CSharp.CSharpCodeProvider");
                            _paramsType = _paramsType ?? assembly.GetType("System.CodeDom.Compiler.CompilerParameters");
                            if (_providerType != null && _paramsType != null)
                                break;
                        }
                        catch
                        {
                        }
                    }
                }

                if (_providerType == null || _paramsType == null)
                {
                    _typeLoadError = "CSharpCodeProvider or CompilerParameters types not found in any loaded assembly";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _typeLoadError = ex.Message;
                return false;
            }
        }

        private static object CompileAndExecute(string code, string className)
        {
            if (!EnsureCodeDomTypes())
                return Response.Error($"CODEDOM_UNAVAILABLE: {_typeLoadError}");

            var provider = Activator.CreateInstance(_providerType);
            try
            {
                var parameters = Activator.CreateInstance(_paramsType);
                _paramsType.GetProperty("GenerateInMemory")?.SetValue(parameters, true, null);
                _paramsType.GetProperty("GenerateExecutable")?.SetValue(parameters, false, null);
                _paramsType.GetProperty("TreatWarningsAsErrors")?.SetValue(parameters, false, null);

                var referencedAssembliesProperty = _paramsType.GetProperty("ReferencedAssemblies");
                var referencedAssemblies = referencedAssembliesProperty?.GetValue(parameters, null);
                var addMethod = referencedAssemblies?.GetType().GetMethod("Add", new[] { typeof(string) });

                var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.IsDynamic)
                        continue;

                    try
                    {
                        var location = assembly.Location;
                        if (!string.IsNullOrEmpty(location) && File.Exists(location) && referenced.Add(location))
                            addMethod?.Invoke(referencedAssemblies, new object[] { location });
                    }
                    catch
                    {
                    }
                }

                var compileMethod = _providerType.GetMethod("CompileAssemblyFromSource", new[] { _paramsType, typeof(string[]) });
                var results = compileMethod?.Invoke(provider, new object[] { parameters, new[] { code } });
                if (results == null)
                    return Response.Error("COMPILATION_NULL_RESULT");

                var resultsType = results.GetType();
                var errors = resultsType.GetProperty("Errors")?.GetValue(results, null);
                var hasErrors = (bool)(errors?.GetType().GetProperty("HasErrors")?.GetValue(errors, null) ?? false);

                if (hasErrors)
                {
                    var errorList = new List<object>();
                    foreach (var error in (IEnumerable)errors)
                    {
                        var errorType = error.GetType();
                        var isWarning = (bool)(errorType.GetProperty("IsWarning")?.GetValue(error, null) ?? false);
                        if (isWarning)
                            continue;
                        errorList.Add(new
                        {
                            line = (int)(errorType.GetProperty("Line")?.GetValue(error, null) ?? 0),
                            column = (int)(errorType.GetProperty("Column")?.GetValue(error, null) ?? 0),
                            text = errorType.GetProperty("ErrorText")?.GetValue(error, null)?.ToString() ?? "Unknown error"
                        });
                    }
                    return Response.Error("COMPILATION_FAILED", new { errors = errorList });
                }

                var compiledAssembly = resultsType.GetProperty("CompiledAssembly")?.GetValue(results, null) as Assembly;
                if (compiledAssembly == null)
                    return Response.Error("COMPILED_ASSEMBLY_MISSING");

                // Prefer IFunplayCommand path: any class in the compiled assembly that implements it
                Type commandType = null;
                try
                {
                    commandType = compiledAssembly.GetTypes()
                        .FirstOrDefault(t => typeof(IFunplayCommand).IsAssignableFrom(t)
                                             && !t.IsInterface && !t.IsAbstract);
                }
                catch (ReflectionTypeLoadException)
                {
                    // Fall through to legacy Run() path
                }
                if (commandType != null)
                    return ExecuteAsCommand(commandType);

                // Legacy path: class with `static Run()`
                var type = compiledAssembly.GetType(className);
                if (type == null)
                    return Response.Error("CLASS_NOT_FOUND",
                        new { className, available = GetTypeNames(compiledAssembly) });

                var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                    return Response.Error("RUN_METHOD_NOT_FOUND", new { className });

                try
                {
                    var result = method.Invoke(null, null);
                    return Response.Success("Executed (legacy Run()).", new { result = result?.ToString() ?? "OK" });
                }
                catch (TargetInvocationException ex)
                {
                    var inner = ex.InnerException ?? ex;
                    Debug.LogError($"[Funplay] Script runtime error: {inner.Message}\n{inner.StackTrace}");
                    return Response.Error("RUNTIME_ERROR",
                        new { message = inner.Message, stack = inner.StackTrace });
                }
            }
            finally
            {
                if (provider is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        private static object ExecuteAsCommand(Type commandType)
        {
            IFunplayCommand instance;
            try { instance = (IFunplayCommand)Activator.CreateInstance(commandType); }
            catch (Exception ex)
            {
                return Response.Error("COMMAND_INSTANTIATION_FAILED",
                    new { type = commandType.FullName, error = ex.Message });
            }

            var ctx = new ExecutionContext();
            try
            {
                instance.Execute(ctx);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Funplay] Command runtime error: {ex.Message}\n{ex.StackTrace}");
                return Response.Error("COMMAND_RUNTIME_ERROR", new
                {
                    message = ex.Message,
                    stack = ex.StackTrace,
                    logs = ctx.Logs,
                    created = ctx.CreatedInstanceIds,
                    modified = ctx.ModifiedInstanceIds,
                    destroyed = ctx.DestroyedInstanceIds
                });
            }

            return Response.Success("Command executed.", new
            {
                logs = ctx.Logs,
                created = ctx.CreatedInstanceIds,
                modified = ctx.ModifiedInstanceIds,
                destroyed = ctx.DestroyedInstanceIds,
                returnValue = ctx.ReturnValue
            });
        }

        private static string[] GetTypeNames(Assembly assembly)
        {
            try
            {
                return Array.ConvertAll(assembly.GetTypes(), t => t.FullName);
            }
            catch
            {
                return new[] { "(unable to list types)" };
            }
        }

        private static string WrapCode(string code, string className, string projectUsings)
        {
            return $@"using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Funplay.Editor.Tools.Scripting;
{projectUsings}
public static class {className}
{{
    public static string Run()
    {{
        {code}
        return ""OK"";
    }}
}}";
        }

        private static string GetProjectNamespaceUsings()
        {
            var namespaces = new HashSet<string>();
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var assetsPath = Path.Combine(projectRoot ?? string.Empty, "Assets");

            if (!Directory.Exists(assetsPath))
                return string.Empty;

            foreach (var file in Directory.GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories))
            {
                try
                {
                    if (file.Contains("~" + Path.DirectorySeparatorChar) ||
                        file.Contains("~" + Path.AltDirectorySeparatorChar))
                    {
                        continue;
                    }

                    var content = File.ReadAllText(file);
                    var matches = Regex.Matches(content, @"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline);
                    foreach (Match match in matches)
                        namespaces.Add(match.Groups[1].Value);
                }
                catch
                {
                }
            }

            if (namespaces.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var ns in namespaces)
                sb.AppendLine($"using {ns};");
            return sb.ToString();
        }

        private static string PrependMissingUsings(string code, string projectUsings)
        {
            if (string.IsNullOrEmpty(projectUsings))
                return code;

            var existing = new HashSet<string>();
            var matches = Regex.Matches(code, @"^\s*using\s+([\w.]+)\s*;", RegexOptions.Multiline);
            foreach (Match match in matches)
                existing.Add(match.Groups[1].Value);

            var missing = new StringBuilder();
            foreach (var line in projectUsings.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                var nsMatch = Regex.Match(trimmed, @"using\s+([\w.]+)\s*;");
                if (nsMatch.Success && !existing.Contains(nsMatch.Groups[1].Value))
                    missing.AppendLine(trimmed);
            }

            return missing.Length == 0 ? code : missing + code;
        }
    }
}
