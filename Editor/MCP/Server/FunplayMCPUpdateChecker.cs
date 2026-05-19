// Copyright (C) Funplay. Licensed under MIT.

using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Funplay.Editor.Services;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityPackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;
using UnityEngine.Networking;

namespace Funplay.Editor.MCP.Server
{
    internal static class FunplayMCPUpdateChecker
    {
        private const string PackageName = "com.gamebooom.unity.mcp";
        private const string PackageRoot = "Packages/com.gamebooom.unity.mcp";
        private const string DefaultAssetRoot = "Assets/unity-mcp";
        private const string GitRepositoryUrl = "https://github.com/FunplayAI/funplay-unity-mcp.git";
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/FunplayAI/funplay-unity-mcp/releases/latest";
        private const string DefaultReleasesUrl = "https://github.com/FunplayAI/funplay-unity-mcp/releases";
        private const string ScriptRootSuffix = "/Editor/MCP/Server/MCPServerService.cs";
        private const double AutoCheckIntervalHours = 6d;
        private const string PrefsPrefix = "Funplay.MCP.UpdateChecker";

        private static bool _stateLoaded;
        private static bool _isChecking;
        private static bool _isUpdating;
        private static bool _updateStarted;
        private static float _progress;
        private static string _statusMessage;
        private static string _latestVersion;
        private static string _latestReleaseUrl;
        private static string _installDescription;
        private static GitHubReleaseResponse _latestRelease;
        private static InstallContext _latestInstallContext;

        public static event Action StateChanged;

        public static UpdateStateSnapshot CurrentState
        {
            get
            {
                EnsureStateLoaded();
                var currentVersion = PackageVersionUtility.CurrentVersion;
                var hasUpdate = HasNewerCachedVersion(currentVersion);
                return new UpdateStateSnapshot(
                    currentVersion,
                    hasUpdate ? _latestVersion : string.Empty,
                    hasUpdate ? _latestReleaseUrl : string.Empty,
                    hasUpdate ? _installDescription : string.Empty,
                    _statusMessage,
                    hasUpdate,
                    _isChecking,
                    _isUpdating,
                    _updateStarted,
                    _progress);
            }
        }

        public static void MaybeCheckForUpdatesInBackground()
        {
            if (Application.isBatchMode)
                return;

            EnsureStateLoaded();
            NotifyStateChanged();

            if (_isChecking || _isUpdating || !IsAutoCheckDue())
                return;

            _ = CheckForUpdatesAsync(false);
        }

        [MenuItem("Funplay/Check for Updates", priority = 51)]
        public static async void CheckForUpdates()
        {
            await CheckForUpdatesAsync(true);
        }

        public static async void UpdateToLatestFromWindow()
        {
            await UpdateLatestKnownAsync();
        }

        private static async Task CheckForUpdatesAsync(bool interactive)
        {
            if (Application.isBatchMode || _isChecking || _isUpdating)
                return;

            EnsureStateLoaded();
            _isChecking = true;
            _updateStarted = false;
            _statusMessage = "Checking for updates...";
            _progress = 0.25f;
            NotifyStateChanged();

            if (interactive)
                EditorUtility.DisplayProgressBar("Funplay MCP", "Checking for updates...", 0.4f);

            try
            {
                var currentVersion = PackageVersionUtility.CurrentVersion;
                var installContext = ResolveInstallContext();
                RecordAutoCheckAttempt();
                var latestRelease = await FetchLatestReleaseAsync();
                if (latestRelease == null)
                {
                    _statusMessage = "Update check failed.";
                    _progress = 0f;
                    NotifyStateChanged();

                    if (interactive)
                    {
                        EditorUtility.DisplayDialog(
                            "Funplay MCP",
                            "Failed to fetch the latest release information from GitHub.",
                            "OK");
                    }

                    return;
                }

                var latestVersion = NormalizeVersion(latestRelease.tag_name);
                var currentSemVer = ParseComparableVersion(currentVersion);
                var latestSemVer = ParseComparableVersion(latestVersion);

                if (latestSemVer > currentSemVer)
                {
                    StoreAvailableUpdate(latestRelease, latestVersion, installContext);
                    _statusMessage = $"Version {latestVersion} is available.";
                    _progress = 0f;
                    NotifyStateChanged();

                    if (!interactive)
                        return;

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
                        await RunUpdateAsync(installContext, latestRelease, latestVersion);
                    }
                    else if (choice == 2)
                    {
                        Application.OpenURL(string.IsNullOrEmpty(latestRelease.html_url) ? DefaultReleasesUrl : latestRelease.html_url);
                    }

                    return;
                }

                if (latestSemVer == currentSemVer)
                {
                    ClearCachedUpdate(false);
                    _statusMessage = string.Empty;
                    _progress = 0f;
                    NotifyStateChanged();

                    if (!interactive)
                        return;

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

                ClearCachedUpdate(false);
                _statusMessage = string.Empty;
                _progress = 0f;
                NotifyStateChanged();

                if (interactive)
                {
                    EditorUtility.DisplayDialog(
                        "Funplay MCP",
                        $"Current version: {currentVersion}\nLatest published release: {latestVersion}\n\nYour local package version appears to be newer than the latest GitHub release.",
                        "OK");
                }
            }
            catch (Exception ex)
            {
                _statusMessage = $"Update check failed: {ex.Message}";
                _progress = 0f;
                NotifyStateChanged();

                if (interactive)
                {
                    EditorUtility.DisplayDialog(
                        "Funplay MCP",
                        $"Failed to check for updates:\n{ex.Message}",
                        "OK");
                }
            }
            finally
            {
                _isChecking = false;
                if (!_isUpdating && !HasNewerCachedVersion(PackageVersionUtility.CurrentVersion))
                    _statusMessage = string.Empty;
                NotifyStateChanged();

                if (interactive)
                    EditorUtility.ClearProgressBar();
            }
        }

        private static async Task UpdateLatestKnownAsync()
        {
            if (Application.isBatchMode || _isUpdating)
                return;

            EnsureStateLoaded();

            var latestRelease = _latestRelease;
            var latestVersion = _latestVersion;
            var installContext = _latestInstallContext;

            if (latestRelease == null || string.IsNullOrEmpty(latestVersion) ||
                !HasNewerCachedVersion(PackageVersionUtility.CurrentVersion))
            {
                _statusMessage = "Preparing update...";
                _progress = 0.1f;
                NotifyStateChanged();
                EditorUtility.DisplayProgressBar("Funplay MCP", "Preparing update...", _progress);

                latestRelease = await FetchLatestReleaseAsync();
                if (latestRelease == null)
                {
                    _statusMessage = "Update failed: could not fetch latest release.";
                    _progress = 0f;
                    NotifyStateChanged();
                    EditorUtility.ClearProgressBar();
                    return;
                }

                latestVersion = NormalizeVersion(latestRelease.tag_name);
                installContext = ResolveInstallContext();
                StoreAvailableUpdate(latestRelease, latestVersion, installContext);
            }

            if (ParseComparableVersion(latestVersion).CompareTo(ParseComparableVersion(PackageVersionUtility.CurrentVersion)) <= 0)
            {
                ClearCachedUpdate(false);
                _statusMessage = "You are up to date.";
                _progress = 0f;
                NotifyStateChanged();
                EditorUtility.ClearProgressBar();
                return;
            }

            await RunUpdateAsync(installContext, latestRelease, latestVersion);
        }

        private static async Task RunUpdateAsync(InstallContext installContext, GitHubReleaseResponse latestRelease, string latestVersion)
        {
            _isUpdating = true;
            _updateStarted = false;
            SetUpdateProgress($"Updating to version {latestVersion}...", 0.15f);

            try
            {
                await UpdateToLatestAsync(installContext, latestRelease, latestVersion);
                _updateStarted = true;
                _statusMessage = $"Update to version {latestVersion} started. Unity may recompile and reload.";
                _progress = 1f;
                NotifyStateChanged();
            }
            catch (Exception ex)
            {
                _updateStarted = false;
                _statusMessage = $"Update failed: {ex.Message}";
                _progress = 0f;
                Debug.LogError($"[Funplay MCP] Update failed: {ex}");
                NotifyStateChanged();
                EditorUtility.DisplayDialog("Funplay MCP", $"Update failed:\n{ex.Message}", "OK");
            }
            finally
            {
                _isUpdating = false;
                NotifyStateChanged();
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
            SetUpdateProgress("Starting Unity Package Manager update...", 0.8f);
            UnityEditor.PackageManager.Client.Add(gitReference);

            EditorUtility.ClearProgressBar();
            SetUpdateProgress("Unity Package Manager update started. Unity may recompile and reload.", 1f);
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

                SetUpdateProgress($"Importing {safeFileName}...", 0.95f);
                AssetDatabase.ImportPackage(tempPackagePath, false);
                AssetDatabase.Refresh();

                EditorUtility.ClearProgressBar();
                SetUpdateProgress($"Imported {safeFileName}. Unity may recompile and reload.", 1f);
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

                    SetUpdateProgress(
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

        private static void SetUpdateProgress(string message, float progress)
        {
            _statusMessage = message;
            _progress = Mathf.Clamp01(progress);
            NotifyStateChanged();
            EditorUtility.DisplayProgressBar("Funplay MCP", message, _progress);
        }

        private static void EnsureStateLoaded()
        {
            if (_stateLoaded)
                return;

            _stateLoaded = true;
            _latestVersion = EditorPrefs.GetString(BuildPrefsKey("LatestVersion"), string.Empty);
            _latestReleaseUrl = EditorPrefs.GetString(BuildPrefsKey("LatestReleaseUrl"), string.Empty);
            _installDescription = EditorPrefs.GetString(BuildPrefsKey("InstallDescription"), string.Empty);

            if (!HasNewerCachedVersion(PackageVersionUtility.CurrentVersion))
                ClearCachedUpdate(false);
        }

        private static bool IsAutoCheckDue()
        {
            var ticksString = EditorPrefs.GetString(BuildPrefsKey("LastAutoCheckUtcTicks"), string.Empty);
            if (!long.TryParse(ticksString, out var ticks))
                return true;

            var lastCheckUtc = new DateTime(ticks, DateTimeKind.Utc);
            return DateTime.UtcNow - lastCheckUtc >= TimeSpan.FromHours(AutoCheckIntervalHours);
        }

        private static void RecordAutoCheckAttempt()
        {
            EditorPrefs.SetString(BuildPrefsKey("LastAutoCheckUtcTicks"), DateTime.UtcNow.Ticks.ToString());
        }

        private static void StoreAvailableUpdate(
            GitHubReleaseResponse latestRelease,
            string latestVersion,
            InstallContext installContext)
        {
            _latestRelease = latestRelease;
            _latestInstallContext = installContext;
            _latestVersion = NormalizeVersion(latestVersion);
            _latestReleaseUrl = string.IsNullOrEmpty(latestRelease?.html_url)
                ? DefaultReleasesUrl
                : latestRelease.html_url;
            _installDescription = installContext.Description;

            EditorPrefs.SetString(BuildPrefsKey("LatestVersion"), _latestVersion);
            EditorPrefs.SetString(BuildPrefsKey("LatestReleaseUrl"), _latestReleaseUrl);
            EditorPrefs.SetString(BuildPrefsKey("InstallDescription"), _installDescription);
        }

        private static void ClearCachedUpdate(bool notify = true)
        {
            _latestVersion = string.Empty;
            _latestReleaseUrl = string.Empty;
            _installDescription = string.Empty;
            _latestRelease = null;
            _latestInstallContext = default;
            _updateStarted = false;

            EditorPrefs.DeleteKey(BuildPrefsKey("LatestVersion"));
            EditorPrefs.DeleteKey(BuildPrefsKey("LatestReleaseUrl"));
            EditorPrefs.DeleteKey(BuildPrefsKey("InstallDescription"));

            if (notify)
                NotifyStateChanged();
        }

        private static bool HasNewerCachedVersion(string currentVersion)
        {
            if (string.IsNullOrEmpty(_latestVersion))
                return false;

            return ParseComparableVersion(_latestVersion) > ParseComparableVersion(currentVersion);
        }

        private static string BuildPrefsKey(string suffix)
        {
            return $"{PrefsPrefix}.{GetProjectHash()}.{suffix}";
        }

        private static string GetProjectHash()
        {
            unchecked
            {
                uint hash = 2166136261;
                var value = Application.dataPath ?? string.Empty;
                for (int i = 0; i < value.Length; i++)
                    hash = (hash ^ value[i]) * 16777619;

                return hash.ToString("x8");
            }
        }

        private static void NotifyStateChanged()
        {
            StateChanged?.Invoke();
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

        public sealed class UpdateStateSnapshot
        {
            public readonly string CurrentVersion;
            public readonly string LatestVersion;
            public readonly string LatestReleaseUrl;
            public readonly string InstallDescription;
            public readonly string StatusMessage;
            public readonly bool HasUpdateAvailable;
            public readonly bool IsChecking;
            public readonly bool IsUpdating;
            public readonly bool UpdateStarted;
            public readonly float Progress;

            public UpdateStateSnapshot(
                string currentVersion,
                string latestVersion,
                string latestReleaseUrl,
                string installDescription,
                string statusMessage,
                bool hasUpdateAvailable,
                bool isChecking,
                bool isUpdating,
                bool updateStarted,
                float progress)
            {
                CurrentVersion = currentVersion;
                LatestVersion = latestVersion;
                LatestReleaseUrl = latestReleaseUrl;
                InstallDescription = installDescription;
                StatusMessage = statusMessage;
                HasUpdateAvailable = hasUpdateAvailable;
                IsChecking = isChecking;
                IsUpdating = isUpdating;
                UpdateStarted = updateStarted;
                Progress = progress;
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
