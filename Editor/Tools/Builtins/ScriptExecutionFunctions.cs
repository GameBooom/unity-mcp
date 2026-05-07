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

        [Description("Primary high-flexibility execution tool. Compiles a C# snippet in memory and runs it on the editor thread. " +
                     "Two templates are supported:\n" +
                     "  1) Recommended: implement IFunplayCommand on a class — receives an ExecutionContext (ctx) " +
                     "with RegisterObjectCreation/RegisterObjectModification/DestroyObject (auto-Undo + tracked) and " +
                     "Log/LogWarning/LogError (returned in the response).\n" +
                     "  2) Legacy: any class with `public static string Run()` — return value becomes the response message.\n" +
                     "Before compiling, the editor's AssetDatabase is refreshed and pending compilation is awaited, " +
                     "so external file edits are picked up automatically without a separate request_recompile.")]
        [SceneEditingTool]
        public static async Task<object> ExecuteCode(
            [ToolParam("C# code to execute. See description for IFunplayCommand vs legacy Run() templates.")] string code)
        {
            try
            {
                await EditorReadyHelper.RefreshAndWaitForReady();
            }
            catch (TimeoutException)
            {
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
                return CompileAndExecute(fullCode, actualClassName);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Funplay] ExecuteCode failed: {ex.Message}");
                return Response.Error($"EXECUTE_CODE_FAILED: {ex.Message}");
            }
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
