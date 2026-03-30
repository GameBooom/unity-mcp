// Copyright (C) GameBooom. Licensed under MIT.

using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace GameBooom.Editor.Services
{
    internal static class PackageVersionUtility
    {
        private static string _cachedVersion;

        public static string CurrentVersion
        {
            get
            {
                if (!string.IsNullOrEmpty(_cachedVersion))
                    return _cachedVersion;

                _cachedVersion = ResolveVersion();
                return _cachedVersion;
            }
        }

        private static string ResolveVersion()
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
            var candidates = new[]
            {
                Path.Combine(projectRoot, "Assets", "unity-mcp", "package.json"),
                Path.Combine(projectRoot, "Packages", "com.gamebooom.unity.mcp", "package.json"),
                Path.Combine(projectRoot, "package.json")
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                var path = candidates[i];
                if (!File.Exists(path))
                    continue;

                var json = File.ReadAllText(path);
                var match = Regex.Match(json, "\"version\"\\s*:\\s*\"(?<version>[^\"]+)\"");
                if (match.Success)
                    return match.Groups["version"].Value;
            }

            return "0.0.0";
        }
    }
}
