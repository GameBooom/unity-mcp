// Copyright (C) GameBooom. Licensed under MIT.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GameBooom.Editor.Tools.Builtins
{
    [ToolProvider("Script")]
    internal static class ScriptExecutionFunctions
    {
        private static Type _providerType;
        private static Type _paramsType;
        private static bool _typesResolved;
        private static string _typeLoadError;

        [Description("Primary high-flexibility execution tool for runtime and editor orchestration. Execute a C# code snippet in the Unity Editor or Play Mode context to inspect live state, query objects and components, validate behavior, build scene content, wire references, batch-create objects, or apply targeted editor changes. Prefer this when the task needs rich multi-step logic or when many small tools would be noisy. The code is compiled in memory and executed immediately via reflection. No .cs files are created and no domain reload is triggered. The code should contain a class with a public static string Run() method.")]
        [SceneEditingTool]
        public static string ExecuteCode(
            [ToolParam("C# code to execute. Must define a class with a public static string Run() method, or be a snippet that can be wrapped into one.")] string code)
        {
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
                Debug.LogError($"[GameBooom] ExecuteCode failed: {ex.Message}");
                return $"Error: {ex.Message}";
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

        private static string CompileAndExecute(string code, string className)
        {
            if (!EnsureCodeDomTypes())
                return $"Error: Cannot compile code - {_typeLoadError}";

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
                    return "Error: Compilation failed to produce results.";

                var resultsType = results.GetType();
                var errors = resultsType.GetProperty("Errors")?.GetValue(results, null);
                var hasErrors = (bool)(errors?.GetType().GetProperty("HasErrors")?.GetValue(errors, null) ?? false);

                if (hasErrors)
                {
                    var sb = new StringBuilder("Compilation errors:\n");
                    foreach (var error in (IEnumerable)errors)
                    {
                        var errorType = error.GetType();
                        var isWarning = (bool)(errorType.GetProperty("IsWarning")?.GetValue(error, null) ?? false);
                        if (isWarning)
                            continue;

                        var line = (int)(errorType.GetProperty("Line")?.GetValue(error, null) ?? 0);
                        var errorText = errorType.GetProperty("ErrorText")?.GetValue(error, null)?.ToString() ?? "Unknown error";
                        sb.AppendLine($"  Line {line}: {errorText}");
                    }

                    return sb.ToString();
                }

                var compiledAssembly = resultsType.GetProperty("CompiledAssembly")?.GetValue(results, null) as Assembly;
                if (compiledAssembly == null)
                    return "Error: Compiled assembly not found.";

                var type = compiledAssembly.GetType(className);
                if (type == null)
                    return $"Error: Type '{className}' not found in compiled assembly. Available types: {string.Join(", ", GetTypeNames(compiledAssembly))}";

                var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                    return $"Error: public static Run() method not found in '{className}'.";

                try
                {
                    var result = method.Invoke(null, null);
                    return result?.ToString() ?? "OK (no return value)";
                }
                catch (TargetInvocationException ex)
                {
                    var inner = ex.InnerException ?? ex;
                    Debug.LogError($"[GameBooom] Script runtime error: {inner.Message}\n{inner.StackTrace}");
                    return $"Runtime error: {inner.Message}\n{inner.StackTrace}";
                }
            }
            finally
            {
                if (provider is IDisposable disposable)
                    disposable.Dispose();
            }
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
