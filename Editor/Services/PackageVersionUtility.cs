// Copyright (C) GameBooom. Licensed under MIT.

using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.PackageManager;
using UnityEngine;

namespace GameBooom.Editor.Services
{
    internal static class PackageVersionUtility
    {
        private const string PackageName = "com.gamebooom.unity.mcp";
        private const string AssetInstallRoot = "Assets/unity-mcp";
        private const string PackageInstallRoot = "Packages/com.gamebooom.unity.mcp";
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
            var packageInfo = PackageInfo.FindForAssetPath(PackageInstallRoot);
            if (packageInfo != null &&
                string.Equals(packageInfo.name, PackageName, System.StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(packageInfo.resolvedPath))
            {
                var resolvedPackageJsonPath = Path.Combine(packageInfo.resolvedPath, "package.json");
                var resolvedVersion = TryReadVersionFromPackageJson(resolvedPackageJsonPath);
                if (!string.IsNullOrEmpty(resolvedVersion))
                    return resolvedVersion;
            }

            var candidates = new[]
            {
                Path.Combine(projectRoot, AssetInstallRoot, "package.json"),
                Path.Combine(projectRoot, PackageInstallRoot, "package.json"),
                Path.Combine(projectRoot, "package.json")
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                var version = TryReadVersionFromPackageJson(candidates[i]);
                if (!string.IsNullOrEmpty(version))
                    return version;
            }

            return "0.0.0";
        }

        private static string TryReadVersionFromPackageJson(string path)
        {
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            var match = Regex.Match(json, "\"version\"\\s*:\\s*\"(?<version>[^\"]+)\"");
            return match.Success ? match.Groups["version"].Value : null;
        }
    }
}
