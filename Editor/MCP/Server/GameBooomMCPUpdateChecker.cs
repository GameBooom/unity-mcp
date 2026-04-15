// Copyright (C) GameBooom. Licensed under MIT.

using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GameBooom.Editor.Services;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityPackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;
using UnityEngine.Networking;

namespace GameBooom.Editor.MCP.Server
{
    internal static class GameBooomMCPUpdateChecker
    {
        private const string PackageName = "com.gamebooom.unity.mcp";
        private const string PackageRoot = "Packages/com.gamebooom.unity.mcp";
        private const string DefaultAssetRoot = "Assets/unity-mcp";
        private const string GitRepositoryUrl = "https://github.com/FunplayAI/funplay-unity-mcp.git";
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/FunplayAI/funplay-unity-mcp/releases/latest";
        private const string DefaultReleasesUrl = "https://github.com/FunplayAI/funplay-unity-mcp/releases";
        private const string ScriptRootSuffix = "/Editor/MCP/Server/MCPServerService.cs";

        [MenuItem("Funplay/Check for Updates", priority = 51)]
        public static async void CheckForUpdates()
        {
            EditorUtility.DisplayProgressBar("Funplay MCP", "Checking for updates...", 0.4f);

            try
            {
                var currentVersion = PackageVersionUtility.CurrentVersion;
                var installContext = ResolveInstallContext();
                var latestRelease = await FetchLatestReleaseAsync();
                if (latestRelease == null)
                {
                    EditorUtility.DisplayDialog(
                        "Funplay MCP",
                        "Failed to fetch the latest release information from GitHub.",
                        "OK");
                    return;
                }

                var latestVersion = NormalizeVersion(latestRelease.tag_name);
                var currentSemVer = ParseComparableVersion(currentVersion);
                var latestSemVer = ParseComparableVersion(latestVersion);

                if (latestSemVer > currentSemVer)
                {
                    var message =
                        $"Current version: {currentVersion}\n" +
                        $"Latest version: {latestVersion}\n" +
                        $"Published: {latestRelease.published_at}\n\n" +
                        $"Install source: {installContext.Description}\n" +
                        $"{BuildUpdateActionMessage(installContext)}";

                    var updateButtonLabel = GetPrimaryActionLabel(installContext);
                    var choice = EditorUtility.DisplayDialogComplex(
                        "Update Available",
                        message,
                        updateButtonLabel,
                        "Close",
                        "View Release");

                    if (choice == 0)
                    {
                        await UpdateToLatestAsync(installContext, latestRelease, latestVersion);
                    }
                    else if (choice == 2)
                    {
                        Application.OpenURL(string.IsNullOrEmpty(latestRelease.html_url) ? DefaultReleasesUrl : latestRelease.html_url);
                    }

                    return;
                }

                if (latestSemVer == currentSemVer)
                {
                    if (EditorUtility.DisplayDialog(
                            "Funplay MCP",
                            $"You are up to date.\n\nCurrent version: {currentVersion}\nLatest version: {latestVersion}\nInstall source: {installContext.Description}",
                            "View Release",
                            "Close"))
                    {
                        Application.OpenURL(string.IsNullOrEmpty(latestRelease.html_url) ? DefaultReleasesUrl : latestRelease.html_url);
                    }

                    return;
                }

                EditorUtility.DisplayDialog(
                    "Funplay MCP",
                    $"Current version: {currentVersion}\nLatest published release: {latestVersion}\n\nYour local package version appears to be newer than the latest GitHub release.",
                    "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "Funplay MCP",
                    $"Failed to check for updates:\n{ex.Message}",
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static async Task UpdateToLatestAsync(InstallContext installContext, GitHubReleaseResponse latestRelease, string latestVersion)
        {
            switch (installContext.Mode)
            {
                case InstallMode.GitPackage:
                    StartGitPackageUpdate(latestVersion);
                    return;
                case InstallMode.UnityPackageImport:
                    await DownloadAndImportUnityPackageAsync(latestRelease, latestVersion);
                    return;
                default:
                    EditorUtility.DisplayDialog(
                        "Funplay MCP",
                        "Automatic updates are only supported for Git installs and unitypackage imports.\n\nOpening the release page instead.",
                        "OK");
                    Application.OpenURL(string.IsNullOrEmpty(latestRelease.html_url) ? DefaultReleasesUrl : latestRelease.html_url);
                    return;
            }
        }

        private static void StartGitPackageUpdate(string latestVersion)
        {
            var gitReference = $"{GitRepositoryUrl}#v{NormalizeVersion(latestVersion)}";
            UnityEditor.PackageManager.Client.Add(gitReference);

            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog(
                "Funplay MCP",
                $"Git update started.\n\nSource: {gitReference}\n\nUnity Package Manager will re-fetch the package. The editor may recompile and reload during the update.",
                "OK");
        }

        private static async Task DownloadAndImportUnityPackageAsync(GitHubReleaseResponse latestRelease, string latestVersion)
        {
            var primaryAsset = latestRelease.GetPrimaryAsset();
            if (primaryAsset == null || string.IsNullOrEmpty(primaryAsset.browser_download_url))
            {
                throw new InvalidOperationException("The latest release does not contain a downloadable .unitypackage asset.");
            }

            var tempDirectory = Path.Combine(Path.GetTempPath(), "FunplayMCP");
            Directory.CreateDirectory(tempDirectory);

            var safeFileName = SanitizeFileName(string.IsNullOrEmpty(primaryAsset.name)
                ? $"FunplayMCP-v{NormalizeVersion(latestVersion)}.unitypackage"
                : primaryAsset.name);
            var tempPackagePath = Path.Combine(tempDirectory, safeFileName);

            try
            {
                await DownloadFileAsync(primaryAsset.browser_download_url, tempPackagePath, primaryAsset.name);

                EditorUtility.DisplayProgressBar("Funplay MCP", $"Importing {safeFileName}...", 0.95f);
                AssetDatabase.ImportPackage(tempPackagePath, false);
                AssetDatabase.Refresh();

                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog(
                    "Funplay MCP",
                    $"Imported {safeFileName}.\n\nLatest version: {latestVersion}",
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                TryDeleteFile(tempPackagePath);
            }
        }

        private static async Task DownloadFileAsync(string url, string destinationPath, string assetName)
        {
            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
            {
                request.timeout = 60;
                request.SetRequestHeader("User-Agent", "Funplay-Unity-MCP");
                request.downloadHandler = new DownloadHandlerFile(destinationPath);

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    var progress = request.downloadProgress;
                    if (progress < 0f)
                        progress = 0f;

                    EditorUtility.DisplayProgressBar(
                        "Funplay MCP",
                        $"Downloading {assetName}...",
                        Mathf.Clamp01(0.55f + progress * 0.35f));
                    await Task.Delay(50);
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new InvalidOperationException($"Failed to download update package: {request.error}");
                }
            }
        }

        private static async Task<GitHubReleaseResponse> FetchLatestReleaseAsync()
        {
            using (var request = UnityWebRequest.Get(LatestReleaseApiUrl))
            {
                request.timeout = 15;
                request.SetRequestHeader("User-Agent", "Funplay-Unity-MCP");
                request.SetRequestHeader("Accept", "application/vnd.github+json");

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                    await Task.Delay(50);

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[Funplay MCP] Update check failed: {request.error}");
                    return null;
                }

                return JsonUtility.FromJson<GitHubReleaseResponse>(request.downloadHandler.text);
            }
        }

        private static InstallContext ResolveInstallContext()
        {
            var packageInfo = UnityPackageInfo.FindForAssetPath(PackageRoot);
            if (packageInfo != null &&
                string.Equals(packageInfo.name, PackageName, StringComparison.OrdinalIgnoreCase))
            {
                if (packageInfo.source == PackageSource.Git)
                {
                    return new InstallContext(
                        InstallMode.GitPackage,
                        packageInfo.assetPath,
                        "Git package");
                }

                return new InstallContext(
                    InstallMode.UnsupportedPackage,
                    packageInfo.assetPath,
                    $"UPM package ({packageInfo.source})");
            }

            var installedRootPath = FindInstalledRootPath();
            if (!string.IsNullOrEmpty(installedRootPath) &&
                installedRootPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return new InstallContext(
                    InstallMode.UnityPackageImport,
                    installedRootPath,
                    "unitypackage import");
            }

            if (AssetDatabase.IsValidFolder(DefaultAssetRoot))
            {
                return new InstallContext(
                    InstallMode.UnityPackageImport,
                    DefaultAssetRoot,
                    "unitypackage import");
            }

            return new InstallContext(InstallMode.Unknown, string.Empty, "unknown");
        }

        private static string FindInstalledRootPath()
        {
            var guids = AssetDatabase.FindAssets("MCPServerService t:MonoScript");
            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path) ||
                    !path.EndsWith(ScriptRootSuffix, StringComparison.OrdinalIgnoreCase) ||
                    path.Length <= ScriptRootSuffix.Length)
                {
                    continue;
                }

                return path.Substring(0, path.Length - ScriptRootSuffix.Length);
            }

            return null;
        }

        private static string NormalizeVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return "0.0.0";

            return version.Trim().TrimStart('v', 'V');
        }

        private static string BuildUpdateActionMessage(InstallContext installContext)
        {
            switch (installContext.Mode)
            {
                case InstallMode.GitPackage:
                    return "A newer version is available.\nChoosing Update Now will re-pull the package from Git through Unity Package Manager.";
                case InstallMode.UnityPackageImport:
                    return "A newer version is available.\nChoosing Update Now will download the latest unitypackage and import it automatically.";
                case InstallMode.UnsupportedPackage:
                    return "A newer version is available.\nThis install is managed by UPM but not from Git, so the update checker will open the release page instead.";
                default:
                    return "A newer version is available.\nThe install source could not be identified reliably, so the update checker will fall back to the release page.";
            }
        }

        private static string GetPrimaryActionLabel(InstallContext installContext)
        {
            switch (installContext.Mode)
            {
                case InstallMode.GitPackage:
                case InstallMode.UnityPackageImport:
                    return "Update Now";
                default:
                    return "Open Release";
            }
        }

        private static ComparableVersion ParseComparableVersion(string version)
        {
            var normalized = NormalizeVersion(version);
            var match = Regex.Match(normalized, @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)");
            if (!match.Success)
                return default;

            return new ComparableVersion(
                ParsePart(match.Groups["major"].Value),
                ParsePart(match.Groups["minor"].Value),
                ParsePart(match.Groups["patch"].Value));
        }

        private static int ParsePart(string value)
        {
            return int.TryParse(value, out var parsed) ? parsed : 0;
        }

        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "FunplayMCP.unitypackage";

            var sanitized = fileName;
            var invalidCharacters = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalidCharacters.Length; i++)
            {
                sanitized = sanitized.Replace(invalidCharacters[i], '_');
            }

            return sanitized;
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort temp file cleanup.
            }
        }

        [Serializable]
        private sealed class GitHubReleaseResponse
        {
            public string tag_name;
            public string html_url;
            public string published_at;
            public GitHubReleaseAsset[] assets;

            public GitHubReleaseAsset GetPrimaryAsset()
            {
                if (assets == null)
                    return null;

                for (int i = 0; i < assets.Length; i++)
                {
                    var asset = assets[i];
                    if (asset == null ||
                        string.IsNullOrEmpty(asset.browser_download_url) ||
                        string.IsNullOrEmpty(asset.name))
                    {
                        continue;
                    }

                    if (asset.name.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
                        return asset;
                }

                return null;
            }
        }

        [Serializable]
        private sealed class GitHubReleaseAsset
        {
            public string name;
            public string browser_download_url;
        }

        private enum InstallMode
        {
            Unknown,
            GitPackage,
            UnityPackageImport,
            UnsupportedPackage
        }

        private readonly struct InstallContext
        {
            public readonly InstallMode Mode;
            public readonly string RootPath;
            public readonly string Description;

            public InstallContext(InstallMode mode, string rootPath, string description)
            {
                Mode = mode;
                RootPath = rootPath;
                Description = string.IsNullOrEmpty(rootPath)
                    ? description
                    : $"{description} ({rootPath})";
            }
        }

        private readonly struct ComparableVersion : IComparable<ComparableVersion>
        {
            private readonly int _major;
            private readonly int _minor;
            private readonly int _patch;

            public ComparableVersion(int major, int minor, int patch)
            {
                _major = major;
                _minor = minor;
                _patch = patch;
            }

            public int CompareTo(ComparableVersion other)
            {
                var majorCompare = _major.CompareTo(other._major);
                if (majorCompare != 0)
                    return majorCompare;

                var minorCompare = _minor.CompareTo(other._minor);
                if (minorCompare != 0)
                    return minorCompare;

                return _patch.CompareTo(other._patch);
            }

            public static bool operator >(ComparableVersion left, ComparableVersion right) => left.CompareTo(right) > 0;
            public static bool operator <(ComparableVersion left, ComparableVersion right) => left.CompareTo(right) < 0;
            public static bool operator ==(ComparableVersion left, ComparableVersion right) => left.CompareTo(right) == 0;
            public static bool operator !=(ComparableVersion left, ComparableVersion right) => !(left == right);

            public override bool Equals(object obj)
            {
                return obj is ComparableVersion other && this == other;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = _major;
                    hashCode = (hashCode * 397) ^ _minor;
                    hashCode = (hashCode * 397) ^ _patch;
                    return hashCode;
                }
            }
        }
    }
}
