// Copyright (C) Funplay. Licensed under MIT.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Funplay.Editor.DI;
using Funplay.Editor.Settings;

namespace Funplay.Editor.MCP.Server
{
    internal class FunplayMCPWindow : EditorWindow
    {
        private ISettingsController _settingsController;
        private MCPServerService _mcpServer;
        private VisualElement _mainContainer;
        private Label _statusLabel;
        private ScrollView _logScrollView;
        private MCPConfigTarget[] _mcpTargets;
        private int _selectedTargetIndex;
        private Label _configStatusLabel;
        private Label _configPathLabel;

        [MenuItem("Funplay/MCP Server")]
        public static void ShowWindow()
        {
            var window = GetWindow<FunplayMCPWindow>("MCP Server");
            window.minSize = new Vector2(360, 400);
            window.Show();
        }

        public void CreateGUI()
        {
            _settingsController = RootScopeServices.Services?.GetService(typeof(ISettingsController))
                as ISettingsController;
            _mcpServer = RootScopeServices.Services?.GetService(typeof(MCPServerService))
                as MCPServerService;

            if (_settingsController == null || _mcpServer == null)
            {
                rootVisualElement.Add(new Label("Failed to initialize services."));
                return;
            }

            BuildUI();
            _mcpServer.InteractionLog.OnEntryAdded += OnLogEntryAdded;
        }

        private void OnDestroy()
        {
            if (_mcpServer?.InteractionLog != null)
                _mcpServer.InteractionLog.OnEntryAdded -= OnLogEntryAdded;
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexGrow = 1;
            rootVisualElement.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);

            _mainContainer = new VisualElement();
            _mainContainer.style.flexGrow = 1;
            _mainContainer.style.paddingLeft = 10;
            _mainContainer.style.paddingRight = 10;
            _mainContainer.style.paddingTop = 10;
            _mainContainer.style.paddingBottom = 10;
            rootVisualElement.Add(_mainContainer);

            // Title
            var title = new Label("MCP Server");
            title.style.fontSize = 16;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = Color.white;
            title.style.marginBottom = 8;
            _mainContainer.Add(title);

            // Status
            _statusLabel = new Label();
            _statusLabel.style.fontSize = 13;
            _statusLabel.style.marginBottom = 10;
            _mainContainer.Add(_statusLabel);
            RefreshStatus();

            // Enable toggle
            var toggle = new Toggle("Enable MCP Server");
            toggle.SetValueWithoutNotify(_settingsController.MCPServerEnabled);
            toggle.RegisterValueChangedCallback(evt =>
            {
                _settingsController.MCPServerEnabled = evt.newValue;
                if (evt.newValue)
                    _ = _mcpServer.StartAsync();
                else
                    _ = _mcpServer.StopAsync();

                EditorApplication.delayCall += () =>
                    EditorApplication.delayCall += RefreshStatus;
            });
            toggle.style.marginBottom = 4;
            _mainContainer.Add(toggle);

            // Port
            var portField = new IntegerField("Server Port");
            portField.SetValueWithoutNotify(_settingsController.MCPServerPort);
            portField.RegisterValueChangedCallback(evt =>
            {
                _settingsController.MCPServerPort = evt.newValue;
            });
            portField.style.marginBottom = 10;
            _mainContainer.Add(portField);

            var toolProfileChoices = new List<string> { "core", "full" };
            var toolProfileField = new PopupField<string>("Tool Exposure", toolProfileChoices,
                Mathf.Max(0, toolProfileChoices.IndexOf(_settingsController.MCPToolExportProfile ?? "core")));
            toolProfileField.SetValueWithoutNotify(_settingsController.MCPToolExportProfile ?? "core");
            toolProfileField.RegisterValueChangedCallback(evt =>
            {
                _settingsController.MCPToolExportProfile = evt.newValue;
            });
            toolProfileField.style.marginBottom = 4;
            _mainContainer.Add(toolProfileField);

            var toolProfileHint = new Label("core reduces tool-list noise for AI clients. full exposes every tool.");
            toolProfileHint.style.fontSize = 10;
            toolProfileHint.style.color = new Color(0.65f, 0.65f, 0.65f);
            toolProfileHint.style.marginBottom = 10;
            _mainContainer.Add(toolProfileHint);

            // One-Click Config Section
            var configLabel = new Label("One-Click MCP Configuration");
            configLabel.style.fontSize = 12;
            configLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            configLabel.style.color = new Color(0.75f, 0.75f, 0.75f);
            configLabel.style.marginBottom = 6;
            _mainContainer.Add(configLabel);

            var homePath = GetUserHomePath();

            _mcpTargets = new[]
            {
                new MCPConfigTarget
                {
                    Name = "Claude Code",
                    ConfigPath = Path.Combine(homePath, ".claude.json"),
                    IncludeTypeField = true
                },
                new MCPConfigTarget
                {
                    Name = "Cursor",
                    ConfigPath = Path.Combine(homePath, ".cursor", "mcp.json"),
                },
                new MCPConfigTarget
                {
                    Name = "VS Code",
                    ConfigPath = GetVSCodeConfigPath(homePath),
                    IncludeTypeField = true,
                    RootKey = "servers"
                },
                new MCPConfigTarget
                {
                    Name = "Trae",
                    ConfigPath = Path.Combine(homePath, ".trae", "mcp.json"),
                },
                new MCPConfigTarget
                {
                    Name = "Kiro",
                    ConfigPath = Path.Combine(homePath, ".kiro", "settings", "mcp.json"),
                    IncludeTypeField = true,
                    RootKey = "mcpServers"
                },
                new MCPConfigTarget
                {
                    Name = "Codex",
                    ConfigPath = Path.Combine(homePath, ".codex", "config.toml"),
                    IsToml = true,
                },
            };

            // Dropdown + Configure button row
            var configRow = new VisualElement();
            configRow.style.flexDirection = FlexDirection.Row;
            configRow.style.alignItems = Align.Center;
            configRow.style.marginBottom = 4;

            var nameList = new List<string>();
            foreach (var t in _mcpTargets) nameList.Add(t.Name);

            _selectedTargetIndex = Mathf.Clamp(_selectedTargetIndex, 0, _mcpTargets.Length - 1);
            var persistedTargetName = _settingsController.MCPSelectedConfigTarget;
            if (!string.IsNullOrWhiteSpace(persistedTargetName))
            {
                var persistedIndex = nameList.FindIndex(name => string.Equals(name, persistedTargetName, StringComparison.OrdinalIgnoreCase));
                if (persistedIndex >= 0)
                    _selectedTargetIndex = persistedIndex;
            }

            var dropdown = new PopupField<string>(nameList, _selectedTargetIndex);
            dropdown.style.flexGrow = 1;
            dropdown.style.height = 26;
            dropdown.RegisterValueChangedCallback(evt =>
            {
                _selectedTargetIndex = nameList.IndexOf(evt.newValue);
                _settingsController.MCPSelectedConfigTarget = evt.newValue;
                BuildUI();
            });
            configRow.Add(dropdown);

            var configBtn = new Button(() =>
            {
                ConfigureMCPForTarget(_mcpTargets[_selectedTargetIndex]);
                RefreshConfigStatus();
            });
            configBtn.text = "Configure";
            configBtn.style.height = 26;
            configBtn.style.width = 80;
            configBtn.style.marginLeft = 4;
            configBtn.style.backgroundColor = new Color(0.2f, 0.5f, 0.3f);
            configBtn.style.color = Color.white;
            configRow.Add(configBtn);

            _mainContainer.Add(configRow);

            _configStatusLabel = new Label();
            _configStatusLabel.style.fontSize = 11;
            _configStatusLabel.style.marginBottom = 2;
            _mainContainer.Add(_configStatusLabel);

            _configPathLabel = new Label();
            _configPathLabel.style.fontSize = 10;
            _configPathLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            _configPathLabel.style.marginBottom = 6;
            _configPathLabel.style.whiteSpace = WhiteSpace.Normal;
            _mainContainer.Add(_configPathLabel);

            RefreshConfigStatus();

            // Interaction Log Section
            BuildLogSection();
        }

        private void BuildLogSection()
        {
            var logHeader = new VisualElement();
            logHeader.style.flexDirection = FlexDirection.Row;
            logHeader.style.alignItems = Align.Center;
            logHeader.style.marginTop = 12;
            logHeader.style.marginBottom = 4;

            var logLabel = new Label("Recent Activity");
            logLabel.style.fontSize = 12;
            logLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            logLabel.style.color = new Color(0.75f, 0.75f, 0.75f);
            logLabel.style.flexGrow = 1;
            logHeader.Add(logLabel);

            var clearBtn = new Button(() =>
            {
                _mcpServer.InteractionLog.Clear();
                _logScrollView.contentContainer.Clear();
            });
            clearBtn.text = "Clear";
            clearBtn.style.height = 20;
            clearBtn.style.width = 50;
            logHeader.Add(clearBtn);

            _mainContainer.Add(logHeader);

            _logScrollView = new ScrollView(ScrollViewMode.Vertical);
            _logScrollView.style.flexGrow = 1;
            _logScrollView.style.backgroundColor = new Color(0.14f, 0.14f, 0.14f);
            _logScrollView.style.borderTopLeftRadius = 4;
            _logScrollView.style.borderTopRightRadius = 4;
            _logScrollView.style.borderBottomLeftRadius = 4;
            _logScrollView.style.borderBottomRightRadius = 4;
            _logScrollView.style.paddingLeft = 6;
            _logScrollView.style.paddingRight = 6;
            _logScrollView.style.paddingTop = 4;
            _logScrollView.style.paddingBottom = 4;
            _mainContainer.Add(_logScrollView);

            var entries = _mcpServer.InteractionLog.GetEntries();
            for (int i = entries.Count - 1; i >= 0; i--)
                AddLogRow(entries[i]);
        }

        private void AddLogRow(MCPLogEntry entry)
        {
            bool isOk = entry.Status == MCPToolCallStatus.Success;
            var accentColor = isOk ? new Color(0.3f, 0.75f, 0.4f) : new Color(0.9f, 0.35f, 0.35f);

            var card = new VisualElement();
            card.style.backgroundColor = new Color(0.19f, 0.19f, 0.19f);
            card.style.borderTopLeftRadius = 4;
            card.style.borderTopRightRadius = 4;
            card.style.borderBottomLeftRadius = 4;
            card.style.borderBottomRightRadius = 4;
            card.style.borderLeftWidth = 3;
            card.style.borderLeftColor = accentColor;
            card.style.paddingLeft = 8;
            card.style.paddingRight = 8;
            card.style.paddingTop = 5;
            card.style.paddingBottom = 5;
            card.style.marginBottom = 3;

            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.alignItems = Align.Center;

            var timeLabel = new Label(entry.Timestamp.ToString("HH:mm:ss"));
            timeLabel.style.fontSize = 10;
            timeLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            timeLabel.style.marginRight = 6;
            timeLabel.style.minWidth = 48;
            topRow.Add(timeLabel);

            var toolLabel = new Label(entry.ToolName);
            toolLabel.style.fontSize = 12;
            toolLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            toolLabel.style.color = new Color(0.88f, 0.88f, 0.88f);
            toolLabel.style.flexGrow = 1;
            topRow.Add(toolLabel);

            var badge = new Label(isOk ? "OK" : "ERR");
            badge.style.fontSize = 9;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.color = Color.white;
            badge.style.backgroundColor = accentColor;
            badge.style.borderTopLeftRadius = 3;
            badge.style.borderTopRightRadius = 3;
            badge.style.borderBottomLeftRadius = 3;
            badge.style.borderBottomRightRadius = 3;
            badge.style.paddingLeft = 5;
            badge.style.paddingRight = 5;
            badge.style.paddingTop = 1;
            badge.style.paddingBottom = 1;
            badge.style.unityTextAlign = TextAnchor.MiddleCenter;
            topRow.Add(badge);

            card.Add(topRow);

            if (!string.IsNullOrEmpty(entry.ResultSummary))
            {
                var summaryLabel = new Label(entry.ResultSummary);
                summaryLabel.style.fontSize = 11;
                summaryLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
                summaryLabel.style.marginTop = 3;
                summaryLabel.style.whiteSpace = WhiteSpace.Normal;
                summaryLabel.style.overflow = Overflow.Hidden;
                card.Add(summaryLabel);
            }

            _logScrollView.contentContainer.Add(card);
        }

        private void OnLogEntryAdded(MCPLogEntry entry)
        {
            EditorApplication.delayCall += () =>
            {
                if (_logScrollView == null) return;
                AddLogRow(entry);
                EditorApplication.delayCall += () =>
                {
                    if (_logScrollView != null)
                        _logScrollView.scrollOffset = new Vector2(0, float.MaxValue);
                };
            };
        }

        private void RefreshStatus()
        {
            if (_statusLabel == null) return;
            if (_mcpServer?.IsRunning == true)
            {
                _statusLabel.text = $"Running on http://127.0.0.1:{_mcpServer.Port}/ ({_settingsController.MCPToolExportProfile ?? "core"})";
                _statusLabel.style.color = new Color(0.4f, 1f, 0.4f);
            }
            else
            {
                _statusLabel.text = "Stopped";
                _statusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            }
        }

        private void RefreshConfigStatus()
        {
            if (_configStatusLabel == null || _configPathLabel == null || _mcpTargets == null) return;
            var idx = Mathf.Clamp(_selectedTargetIndex, 0, _mcpTargets.Length - 1);
            var target = _mcpTargets[idx];
            bool exists = File.Exists(target.ConfigPath);

            _configStatusLabel.text = exists ? "Status: Configured" : "Status: Not configured";
            _configStatusLabel.style.color = exists
                ? new Color(0.4f, 1f, 0.4f)
                : new Color(1f, 0.6f, 0.4f);

            _configPathLabel.text = target.ConfigPath;
        }

        private struct MCPConfigTarget
        {
            public string Name;
            public string ConfigPath;
            public string RootKey;
            public bool IsToml;
            public bool IncludeTypeField;
        }

        private void ConfigureMCPForTarget(MCPConfigTarget target)
        {
            try
            {
                var dir = Path.GetDirectoryName(target.ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (target.IsToml)
                {
                    ConfigureTomlTarget(target);
                }
                else
                {
                    ConfigureJsonTarget(target);
                }

                EditorUtility.DisplayDialog(
                    "MCP Configuration",
                    $"MCP configuration written to:\n{target.ConfigPath}\n\n" +
                    $"Please restart {target.Name} for it to take effect.",
                    "OK");

                BuildUI();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "MCP Configuration Error",
                    $"Configuration failed:\n{ex.Message}",
                    "OK");
            }
        }

        private void ConfigureJsonTarget(MCPConfigTarget target)
        {
            var rootKey = string.IsNullOrEmpty(target.RootKey) ? "mcpServers" : target.RootKey;
            var serverName = "funplay";
            var entry = CreateHttpEntry(target);
            Dictionary<string, object> root;

            if (File.Exists(target.ConfigPath))
            {
                var existingJson = File.ReadAllText(target.ConfigPath);
                var parsed = SimpleJsonHelper.Deserialize(existingJson) as Dictionary<string, object>;

                if (parsed != null && parsed.ContainsKey(rootKey))
                {
                    root = parsed;
                    var servers = root[rootKey] as Dictionary<string, object>;
                    if (servers != null)
                        servers[serverName] = entry;
                    else
                        root[rootKey] = new Dictionary<string, object> { [serverName] = entry };
                }
                else
                {
                    root = parsed ?? new Dictionary<string, object>();
                    root[rootKey] = new Dictionary<string, object> { [serverName] = entry };
                }
            }
            else
            {
                root = new Dictionary<string, object>
                {
                    [rootKey] = new Dictionary<string, object> { [serverName] = entry }
                };
            }

            File.WriteAllText(target.ConfigPath, SimpleJsonHelper.Serialize(root));
        }

        private void ConfigureTomlTarget(MCPConfigTarget target)
        {
            var sectionHeader = "[mcp_servers.funplay]";
            var tomlSection = CreateTomlSection(target);
            var content = File.Exists(target.ConfigPath) ? File.ReadAllText(target.ConfigPath) : "";

            if (content.Contains(sectionHeader))
            {
                // Replace existing section: find start, find next section or EOF, replace
                var startIdx = content.IndexOf(sectionHeader, StringComparison.Ordinal);
                var afterHeader = startIdx + sectionHeader.Length;
                var nextSection = content.IndexOf("\n[", afterHeader, StringComparison.Ordinal);
                var endIdx = nextSection >= 0 ? nextSection : content.Length;
                content = content.Substring(0, startIdx) + tomlSection + content.Substring(endIdx);
            }
            else
            {
                // Append
                if (content.Length > 0 && !content.EndsWith("\n"))
                    content += "\n";
                content += "\n" + tomlSection;
            }

            File.WriteAllText(target.ConfigPath, content);
        }

        private Dictionary<string, object> CreateHttpEntry(MCPConfigTarget target)
        {
            var entry = new Dictionary<string, object>
            {
                ["url"] = GetServerUrl()
            };

            if (target.IncludeTypeField)
                entry["type"] = "http";

            return entry;
        }

        private string CreateTomlSection(MCPConfigTarget target)
        {
            if (!target.IsToml)
                return string.Empty;

            return $"[mcp_servers.funplay]\nurl = \"{GetServerUrl()}\"\n";
        }

        private string GetServerUrl()
        {
            var port = _mcpServer != null && _mcpServer.IsRunning
                ? _mcpServer.Port
                : _settingsController.MCPServerPort;
            return $"http://127.0.0.1:{port}/";
        }

        private static string GetProjectRootPath()
        {
            return Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
        }

        private static string GetUserHomePath()
        {
            var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(homePath))
                return homePath;

            var homeDrive = Environment.GetEnvironmentVariable("HOMEDRIVE");
            var homeDir = Environment.GetEnvironmentVariable("HOMEPATH");
            if (!string.IsNullOrEmpty(homeDrive) && !string.IsNullOrEmpty(homeDir))
                return homeDrive + homeDir;

            return Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        }

        private static string GetVSCodeConfigPath(string homePath)
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    if (!string.IsNullOrEmpty(appData))
                        return Path.Combine(appData, "Code", "User", "mcp.json");
                    break;

                case RuntimePlatform.OSXEditor:
                    var macPrimaryPath = Path.Combine(homePath, "Library", "Application Support", "Code", "User", "mcp.json");
                    var macPrimaryDirectory = Path.GetDirectoryName(macPrimaryPath);
                    if (File.Exists(macPrimaryPath) || (!string.IsNullOrEmpty(macPrimaryDirectory) && Directory.Exists(macPrimaryDirectory)))
                        return macPrimaryPath;

                    return Path.Combine(homePath, ".vscode", "mcp.json");

                case RuntimePlatform.LinuxEditor:
                    return Path.Combine(homePath, ".config", "Code", "User", "mcp.json");
            }

            return Path.Combine(homePath, ".vscode", "mcp.json");
        }
    }

    internal class FunplayProjectSkillsWindow : EditorWindow
    {
        private readonly Dictionary<string, Toggle> _optionalSkillToggles = new Dictionary<string, Toggle>(StringComparer.OrdinalIgnoreCase);
        private ISettingsController _settingsController;
        private VisualElement _mainContainer;
        private Label _statusLabel;
        private Label _manifestPathLabel;
        private VisualElement _generatedFilesContainer;
        private Toggle _enableCurrentPlatformToggle;
        private PopupField<string> _platformDropdown;
        private string[] _platformTargets;
        private int _selectedTargetIndex;

        [MenuItem("Funplay/Project Skills (Experimental)")]
        public static void ShowWindow()
        {
            var window = GetWindow<FunplayProjectSkillsWindow>("Project Skills (Experimental)");
            window.minSize = new Vector2(420, 520);
            window.Show();
        }

        public void CreateGUI()
        {
            _settingsController = RootScopeServices.Services?.GetService(typeof(ISettingsController))
                as ISettingsController;

            if (_settingsController == null)
            {
                rootVisualElement.Add(new Label("Failed to initialize services."));
                return;
            }

            BuildUI();
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexGrow = 1;
            rootVisualElement.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);

            _mainContainer = new VisualElement();
            _mainContainer.style.flexGrow = 1;
            _mainContainer.style.paddingLeft = 10;
            _mainContainer.style.paddingRight = 10;
            _mainContainer.style.paddingTop = 10;
            _mainContainer.style.paddingBottom = 10;
            rootVisualElement.Add(_mainContainer);

            var header = CreateSection();
            header.style.marginBottom = 10;
            var title = new Label("Project Skills (Experimental)");
            title.style.fontSize = 17;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = Color.white;
            title.style.marginBottom = 4;
            header.Add(title);

            var hintLabel = new Label("Experimental: configure project-level skills for supported AI clients. Built-in skills are always installed. Optional skills can be added or removed.");
            hintLabel.style.fontSize = 11;
            hintLabel.style.color = new Color(0.65f, 0.65f, 0.65f);
            hintLabel.style.whiteSpace = WhiteSpace.Normal;
            header.Add(hintLabel);
            _mainContainer.Add(header);

            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1;
            scrollView.style.marginBottom = 8;
            _mainContainer.Add(scrollView);

            _mainContainer = scrollView.contentContainer;

            BuildPlatformSection();
            BuildSkillsSection();
            BuildStatusSection();
            BuildActionsSection(rootVisualElement);

            RefreshStatus();
        }

        private void BuildPlatformSection()
        {
            var section = CreateSection();
            section.Add(CreateSectionHeader("Current Platform"));

            _platformTargets = new[] { "Claude Code", "Cursor", "VS Code", "Trae", "Kiro", "Codex" };
            _selectedTargetIndex = Mathf.Clamp(_selectedTargetIndex, 0, _platformTargets.Length - 1);
            var persistedTargetName = _settingsController.MCPSelectedConfigTarget;
            if (!string.IsNullOrWhiteSpace(persistedTargetName))
            {
                var persistedIndex = Array.FindIndex(_platformTargets, name => string.Equals(name, persistedTargetName, StringComparison.OrdinalIgnoreCase));
                if (persistedIndex >= 0)
                    _selectedTargetIndex = persistedIndex;
            }

            _platformDropdown = new PopupField<string>(new List<string>(_platformTargets), _selectedTargetIndex);
            _platformDropdown.style.marginBottom = 6;
            _platformDropdown.RegisterValueChangedCallback(evt =>
            {
                _selectedTargetIndex = Array.IndexOf(_platformTargets, evt.newValue);
                _settingsController.MCPSelectedConfigTarget = evt.newValue;
                BuildUI();
            });
            section.Add(_platformDropdown);

            var currentPlatformId = GetCurrentSkillsPlatformId();
            var currentPlatformSupported = !string.IsNullOrEmpty(currentPlatformId);
            var manifest = ProjectSkillsManager.LoadManifest(GetProjectRootPath());

            _enableCurrentPlatformToggle = new Toggle("Enable skills for current platform");
            _enableCurrentPlatformToggle.SetValueWithoutNotify(
                currentPlatformSupported &&
                manifest.platforms.Contains(currentPlatformId, StringComparer.OrdinalIgnoreCase));
            _enableCurrentPlatformToggle.SetEnabled(currentPlatformSupported);
            _enableCurrentPlatformToggle.style.marginBottom = 4;
            section.Add(_enableCurrentPlatformToggle);

            if (!currentPlatformSupported)
            {
                section.Add(CreateHint("Project skills integration is not available for this platform yet. Supported platforms: Codex, Claude Code, Cursor.", new Color(1f, 0.75f, 0.45f)));
            }

            _mainContainer.Add(section);
        }

        private void BuildSkillsSection()
        {
            var manifest = ProjectSkillsManager.LoadManifest(GetProjectRootPath());
            _optionalSkillToggles.Clear();

            var builtInSection = CreateSection();
            builtInSection.Add(CreateSectionHeader("Built-in Skills"));

            foreach (var skill in ProjectSkillsManager.GetBuiltInSkills())
            {
                builtInSection.Add(CreateSkillRow(skill.Title, skill.Description, "Required"));
            }
            _mainContainer.Add(builtInSection);

            var optionalSection = CreateSection();
            optionalSection.Add(CreateSectionHeader("Optional Skills"));

            foreach (var skill in ProjectSkillsManager.GetOptionalSkills())
            {
                var toggle = new Toggle(skill.Title);
                toggle.SetValueWithoutNotify(manifest.optionalSkills.Contains(skill.Id, StringComparer.OrdinalIgnoreCase));
                toggle.style.marginBottom = 0;
                toggle.style.unityFontStyleAndWeight = FontStyle.Bold;
                optionalSection.Add(toggle);

                var description = CreateHint(skill.Description, new Color(0.58f, 0.58f, 0.58f));
                description.style.marginLeft = 18;
                description.style.marginBottom = 6;
                optionalSection.Add(description);

                _optionalSkillToggles[skill.Id] = toggle;
            }

            optionalSection.Add(CreateHint("Uncheck optional skills and click Apply Skills to remove them. Built-in skills cannot be removed.", new Color(0.65f, 0.65f, 0.65f)));
            _mainContainer.Add(optionalSection);
        }

        private void BuildActionsSection(VisualElement root)
        {
            var actionRow = new VisualElement();
            actionRow.style.flexDirection = FlexDirection.Row;
            actionRow.style.alignItems = Align.Center;
            actionRow.style.paddingLeft = 10;
            actionRow.style.paddingRight = 10;
            actionRow.style.paddingTop = 8;
            actionRow.style.paddingBottom = 8;
            actionRow.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f);

            var applyButton = new Button(() =>
            {
                ApplyProjectSkillsConfiguration();
                RefreshStatus();
            });
            applyButton.text = "Apply Skills";
            applyButton.style.height = 26;
            applyButton.style.width = 100;
            applyButton.style.backgroundColor = new Color(0.25f, 0.45f, 0.65f);
            applyButton.style.color = Color.white;
            actionRow.Add(applyButton);

            var refreshButton = new Button(RefreshStatus);
            refreshButton.text = "Refresh";
            refreshButton.style.height = 26;
            refreshButton.style.width = 80;
            refreshButton.style.marginLeft = 6;
            actionRow.Add(refreshButton);

            root.Add(actionRow);
        }

        private void BuildStatusSection()
        {
            var section = CreateSection();
            section.Add(CreateSectionHeader("Installed Files"));

            _statusLabel = new Label();
            _statusLabel.style.fontSize = 11;
            _statusLabel.style.marginBottom = 4;
            section.Add(_statusLabel);

            _manifestPathLabel = new Label();
            _manifestPathLabel.style.fontSize = 10;
            _manifestPathLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            _manifestPathLabel.style.marginBottom = 6;
            _manifestPathLabel.style.whiteSpace = WhiteSpace.Normal;
            section.Add(_manifestPathLabel);

            _generatedFilesContainer = new VisualElement();
            section.Add(_generatedFilesContainer);
            _mainContainer.Add(section);
        }

        private void RefreshStatus()
        {
            if (_statusLabel == null || _manifestPathLabel == null || _generatedFilesContainer == null)
                return;

            var projectRoot = GetProjectRootPath();
            var manifest = ProjectSkillsManager.LoadManifest(projectRoot);
            var installedSkills = ProjectSkillsManager.GetInstalledSkills(manifest);
            var currentPlatformId = GetCurrentSkillsPlatformId();
            var currentPlatformDisplayName = GetCurrentSkillsPlatformDisplayName();
            var currentPlatformSupported = !string.IsNullOrEmpty(currentPlatformId);
            var currentPlatformConfigured = currentPlatformSupported &&
                                            manifest.platforms.Contains(currentPlatformId, StringComparer.OrdinalIgnoreCase);

            if (_enableCurrentPlatformToggle != null)
            {
                _enableCurrentPlatformToggle.SetEnabled(currentPlatformSupported);
                _enableCurrentPlatformToggle.SetValueWithoutNotify(currentPlatformConfigured);
            }

            if (!currentPlatformSupported)
            {
                _statusLabel.text = $"Status: Unsupported current platform | Built-in: {ProjectSkillsManager.GetBuiltInSkills().Count} | Optional installed: {manifest.optionalSkills.Count}";
                _statusLabel.style.color = new Color(1f, 0.6f, 0.4f);
            }
            else if (!currentPlatformConfigured)
            {
                _statusLabel.text = $"Status: Not configured for {currentPlatformDisplayName} | Built-in: {ProjectSkillsManager.GetBuiltInSkills().Count} | Optional installed: {manifest.optionalSkills.Count}";
                _statusLabel.style.color = new Color(1f, 0.6f, 0.4f);
            }
            else
            {
                _statusLabel.text = $"Status: Configured for {currentPlatformDisplayName} | Skills: {installedSkills.Count}";
                _statusLabel.style.color = new Color(0.4f, 1f, 0.4f);
            }

            _manifestPathLabel.text = $"Manifest: {ProjectSkillsManager.GetManifestPath(projectRoot)}";
            RefreshGeneratedFiles(projectRoot, manifest, currentPlatformId, currentPlatformDisplayName);
        }

        private void ApplyProjectSkillsConfiguration()
        {
            var projectRoot = GetProjectRootPath();
            var currentPlatformId = GetCurrentSkillsPlatformId();
            var selectedOptionalSkills = _optionalSkillToggles
                .Where(entry => entry.Value.value)
                .Select(entry => entry.Key)
                .ToArray();

            try
            {
                if (string.IsNullOrEmpty(currentPlatformId))
                {
                    EditorUtility.DisplayDialog(
                        "Project Skills Configuration",
                        "Project skills are not supported for the currently selected platform yet.\n\nPlease select Codex, Claude Code, or Cursor.",
                        "OK");
                    return;
                }

                var manifest = ProjectSkillsManager.LoadManifest(projectRoot);
                var selectedPlatforms = new HashSet<string>(manifest.platforms, StringComparer.OrdinalIgnoreCase);
                if (_enableCurrentPlatformToggle != null && _enableCurrentPlatformToggle.value)
                    selectedPlatforms.Add(currentPlatformId);
                else
                    selectedPlatforms.Remove(currentPlatformId);

                var conflictPaths = ProjectSkillsManager.GetPlatformConflictPaths(projectRoot, selectedPlatforms);
                if (conflictPaths.Length > 0)
                {
                    var overwrite = EditorUtility.DisplayDialog(
                        "Project Skills Configuration",
                        "Existing non-managed project instruction files were found:\n\n" +
                        string.Join("\n", conflictPaths) +
                        "\n\nOverwrite them with Funplay-managed files?",
                        "Overwrite",
                        "Cancel");

                    if (!overwrite)
                        return;
                }

                ProjectSkillsManager.ApplyConfiguration(projectRoot, selectedPlatforms, selectedOptionalSkills);

                EditorUtility.DisplayDialog(
                    "Project Skills Configuration",
                    "Project skills configuration updated successfully.\n\n" +
                    $"Manifest:\n{ProjectSkillsManager.GetManifestPath(projectRoot)}",
                    "OK");

                BuildUI();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "Project Skills Configuration Error",
                    $"Configuration failed:\n{ex.Message}",
                    "OK");
            }
        }

        private string GetCurrentSkillsPlatformId()
        {
            if (_platformTargets == null || _platformTargets.Length == 0)
                return null;

            var idx = Mathf.Clamp(_selectedTargetIndex, 0, _platformTargets.Length - 1);
            return MapTargetNameToSkillsPlatformId(_platformTargets[idx]);
        }

        private string GetCurrentSkillsPlatformDisplayName()
        {
            if (_platformTargets == null || _platformTargets.Length == 0)
                return "Unknown";

            var idx = Mathf.Clamp(_selectedTargetIndex, 0, _platformTargets.Length - 1);
            return _platformTargets[idx];
        }

        private static string MapTargetNameToSkillsPlatformId(string targetName)
        {
            switch (targetName?.Trim())
            {
                case "Codex":
                    return "codex";
                case "Claude Code":
                    return "claude";
                case "Cursor":
                    return "cursor";
                default:
                    return null;
            }
        }

        private void RefreshGeneratedFiles(
            string projectRoot,
            ProjectSkillsManager.ProjectSkillsManifest manifest,
            string currentPlatformId,
            string currentPlatformDisplayName)
        {
            _generatedFilesContainer.Clear();

            if (string.IsNullOrEmpty(currentPlatformId))
            {
                _generatedFilesContainer.Add(CreateHint($"Generated files for {currentPlatformDisplayName}: not supported yet.", new Color(0.6f, 0.6f, 0.6f)));
                return;
            }

            var paths = ProjectSkillsManager.GetGeneratedPathsForPlatform(projectRoot, manifest, currentPlatformId);
            if (paths.Count == 0)
            {
                _generatedFilesContainer.Add(CreateHint($"Generated files for {currentPlatformDisplayName}: none.", new Color(0.6f, 0.6f, 0.6f)));
                return;
            }

            _generatedFilesContainer.Add(CreateHint($"Generated files for {currentPlatformDisplayName}:", new Color(0.7f, 0.7f, 0.7f)));
            foreach (var path in paths)
            {
                var exists = File.Exists(path) || Directory.Exists(path);
                var row = new Label($"{(exists ? "OK" : "Missing")}  {path}");
                row.style.fontSize = 10;
                row.style.color = exists ? new Color(0.55f, 0.85f, 0.55f) : new Color(1f, 0.65f, 0.45f);
                row.style.marginLeft = 8;
                row.style.marginBottom = 2;
                row.style.whiteSpace = WhiteSpace.Normal;
                _generatedFilesContainer.Add(row);
            }
        }

        private static string GetProjectRootPath()
        {
            return Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
        }

        private static VisualElement CreateSection()
        {
            var section = new VisualElement();
            section.style.backgroundColor = new Color(0.205f, 0.205f, 0.205f);
            section.style.borderTopLeftRadius = 5;
            section.style.borderTopRightRadius = 5;
            section.style.borderBottomLeftRadius = 5;
            section.style.borderBottomRightRadius = 5;
            section.style.paddingLeft = 8;
            section.style.paddingRight = 8;
            section.style.paddingTop = 7;
            section.style.paddingBottom = 7;
            section.style.marginBottom = 8;
            return section;
        }

        private static Label CreateSectionHeader(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 12;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new Color(0.82f, 0.82f, 0.82f);
            label.style.marginBottom = 5;
            return label;
        }

        private static Label CreateHint(string text, Color color)
        {
            var label = new Label(text);
            label.style.fontSize = 10;
            label.style.color = color;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 4;
            return label;
        }

        private static VisualElement CreateSkillRow(string title, string description, string badgeText)
        {
            var row = new VisualElement();
            row.style.backgroundColor = new Color(0.17f, 0.17f, 0.17f);
            row.style.borderTopLeftRadius = 4;
            row.style.borderTopRightRadius = 4;
            row.style.borderBottomLeftRadius = 4;
            row.style.borderBottomRightRadius = 4;
            row.style.paddingLeft = 7;
            row.style.paddingRight = 7;
            row.style.paddingTop = 5;
            row.style.paddingBottom = 5;
            row.style.marginBottom = 4;

            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;

            var titleLabel = new Label(title);
            titleLabel.style.flexGrow = 1;
            titleLabel.style.fontSize = 11;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = new Color(0.88f, 0.88f, 0.88f);
            titleRow.Add(titleLabel);

            var badge = new Label(badgeText);
            badge.style.fontSize = 9;
            badge.style.color = Color.white;
            badge.style.backgroundColor = new Color(0.32f, 0.48f, 0.7f);
            badge.style.borderTopLeftRadius = 3;
            badge.style.borderTopRightRadius = 3;
            badge.style.borderBottomLeftRadius = 3;
            badge.style.borderBottomRightRadius = 3;
            badge.style.paddingLeft = 5;
            badge.style.paddingRight = 5;
            badge.style.paddingTop = 1;
            badge.style.paddingBottom = 1;
            titleRow.Add(badge);

            row.Add(titleRow);

            var descriptionLabel = new Label(description);
            descriptionLabel.style.fontSize = 10;
            descriptionLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            descriptionLabel.style.marginTop = 3;
            descriptionLabel.style.whiteSpace = WhiteSpace.Normal;
            row.Add(descriptionLabel);

            return row;
        }
    }

    internal static class ProjectSkillsManager
    {
        internal const string ManagedMarker = "<!-- Funplay Unity MCP managed project skills -->";

        private const string ManifestDirectory = ".funplay/skills";
        private const string ManifestFileName = "manifest.json";

        private static readonly string[] SupportedPlatforms = { "codex", "claude", "cursor" };

        private static readonly SkillDefinition[] SkillCatalog =
        {
            new SkillDefinition(
                "using-funplay-skills",
                "Using FunPlay Skills",
                "Learn how to discover, choose, and apply FunPlay workflows in an agent session.",
                true,
                "Use this skill when the user needs help choosing the right FunPlay workflow or combining multiple workflows.",
                new[]
                {
                    "Identify the asset type or workflow first.",
                    "Prefer the simplest local workflow that solves the immediate task.",
                    "Recommend exact commands when a workflow can be run directly.",
                    "If no installed workflow fits, say which additional skill should be enabled."
                }),
            new SkillDefinition(
                "unity-prefab-workflow",
                "Unity Prefab Workflow",
                "Safely plan and review Unity prefab, scene, and serialized asset edits.",
                true,
                "Use this skill when the task touches Unity `.prefab`, `.unity`, `.mat`, `.asset`, or related serialized files.",
                new[]
                {
                    "Treat Unity YAML assets as serialization-sensitive.",
                    "Preserve `.meta` file relationships and GUID stability.",
                    "Prefer focused edits to one prefab, scene, or material group at a time.",
                    "Call out in-editor verification steps after file changes."
                }),
            new SkillDefinition(
                "gameplay-prototyping",
                "Gameplay Prototyping",
                "Turn a rough game concept into a small, buildable prototype spec.",
                false,
                "Use this skill when the user has a game idea but not yet a crisp first-playable definition.",
                new[]
                {
                    "Identify the core player verb and shortest fun loop.",
                    "Cut anything that does not support the first playable.",
                    "Define minimum scenes, mechanics, UI, and assets.",
                    "End with milestones and acceptance criteria."
                }),
            new SkillDefinition(
                "level-design-review",
                "Level Design Review",
                "Review flow, readability, guidance, and pacing in a level or encounter layout.",
                false,
                "Use this skill when the user wants critique on level structure, encounter pacing, or player guidance.",
                new[]
                {
                    "Check readability of goals and navigation.",
                    "Call out pacing spikes and dead zones.",
                    "Separate mandatory fixes from polish suggestions.",
                    "End with actionable revisions."
                }),
            new SkillDefinition(
                "sprite-sheet",
                "Sprite Sheet",
                "Split one sprite sheet into frame images and plan clean export workflows.",
                false,
                "Use this skill when the task involves slicing or preparing sprite-sheet based art assets.",
                new[]
                {
                    "Clarify rows, columns, frame order, and padding assumptions.",
                    "Prefer deterministic naming for exported frames.",
                    "Call out how the output should be imported into the target engine."
                }),
            new SkillDefinition(
                "normal-map",
                "Normal Map",
                "Generate or review normal-map workflows for 2D and 3D game textures.",
                false,
                "Use this skill when the task involves creating or validating normal maps from diffuse textures.",
                new[]
                {
                    "Check source texture suitability first.",
                    "State expected output format and engine import notes.",
                    "Call out roughness, lighting, and artifact risks."
                }),
            new SkillDefinition(
                "audio-format-convert",
                "Audio Format Convert",
                "Convert game audio between wav, ogg, and mp3 with pipeline awareness.",
                false,
                "Use this skill when the task is converting audio assets for game integration.",
                new[]
                {
                    "Confirm target format and playback context.",
                    "Preserve loop quality and avoid accidental clipping.",
                    "Call out if `ffmpeg` or another local binary is required."
                }),
            new SkillDefinition(
                "game-audio-polish",
                "Game Audio Polish",
                "Review game audio assets for loudness, looping, and implementation readiness.",
                false,
                "Use this skill when the user wants critique on SFX or music readiness for shipping or prototype use.",
                new[]
                {
                    "Review loudness consistency and loop seams.",
                    "Identify implementation issues before engine import.",
                    "Separate blocking problems from polish notes."
                }),
            new SkillDefinition(
                "texture-atlas",
                "Texture Atlas",
                "Plan atlas grouping, naming, padding, and packing strategy for 2D/UI assets.",
                false,
                "Use this skill when multiple textures or UI sprites need to be packed and organized as an atlas.",
                new[]
                {
                    "Group by usage pattern and runtime constraints.",
                    "Call out padding and bleed considerations.",
                    "Define naming and manifest expectations clearly."
                }),
            new SkillDefinition(
                "ui-slicing-checklist",
                "UI Slicing Checklist",
                "Review UI sprites for slicing, nine-patch, and export readiness.",
                false,
                "Use this skill when the task involves preparing UI art for reliable scaling and slicing behavior.",
                new[]
                {
                    "Check nine-slice suitability before import.",
                    "Call out anchor, padding, and scaling assumptions.",
                    "Highlight likely engine-side verification steps."
                }),
        };

        internal static IReadOnlyList<SkillDefinition> GetBuiltInSkills()
        {
            return SkillCatalog.Where(skill => skill.IsBuiltIn).ToArray();
        }

        internal static IReadOnlyList<SkillDefinition> GetOptionalSkills()
        {
            return SkillCatalog.Where(skill => !skill.IsBuiltIn).ToArray();
        }

        internal static IReadOnlyList<string> GetSupportedPlatforms()
        {
            return SupportedPlatforms;
        }

        internal static ProjectSkillsManifest LoadManifest(string projectRoot)
        {
            var manifestPath = GetManifestPath(projectRoot);
            try
            {
                if (File.Exists(manifestPath))
                {
                    var json = File.ReadAllText(manifestPath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var loaded = JsonUtility.FromJson<ProjectSkillsManifest>(json);
                        if (loaded != null)
                            return NormalizeManifest(loaded);
                    }
                }
            }
            catch
            {
            }

            return CreateDefaultManifest();
        }

        internal static void SaveManifest(string projectRoot, ProjectSkillsManifest manifest)
        {
            var normalized = NormalizeManifest(manifest);
            var manifestPath = GetManifestPath(projectRoot);
            var directory = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(manifestPath, JsonUtility.ToJson(normalized, true));
        }

        internal static string GetManifestPath(string projectRoot)
        {
            return Path.Combine(projectRoot, ManifestDirectory, ManifestFileName);
        }

        internal static string GetCodexAgentsPath(string projectRoot)
        {
            return Path.Combine(projectRoot, "AGENTS.md");
        }

        internal static string GetClaudeInstructionsPath(string projectRoot)
        {
            return Path.Combine(projectRoot, "CLAUDE.md");
        }

        internal static string GetCursorRulesPath(string projectRoot)
        {
            return Path.Combine(projectRoot, ".cursor", "rules");
        }

        internal static string GetCodexSkillsRoot(string projectRoot)
        {
            return Path.Combine(projectRoot, ".agents", "skills");
        }

        internal static string GetClaudeCommandsRoot(string projectRoot)
        {
            return Path.Combine(projectRoot, ".claude", "commands");
        }

        internal static void ApplyConfiguration(string projectRoot, IEnumerable<string> selectedPlatforms, IEnumerable<string> selectedOptionalSkills)
        {
            var manifest = new ProjectSkillsManifest
            {
                platforms = selectedPlatforms?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
                optionalSkills = selectedOptionalSkills?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>()
            };

            SaveManifest(projectRoot, manifest);

            var normalized = LoadManifest(projectRoot);
            SyncCodex(projectRoot, normalized);
            SyncClaude(projectRoot, normalized);
            SyncCursor(projectRoot, normalized);
        }

        internal static bool IsPlatformConfigured(string projectRoot, string platformId)
        {
            var manifest = LoadManifest(projectRoot);
            return manifest.platforms.Contains(platformId, StringComparer.OrdinalIgnoreCase);
        }

        internal static IReadOnlyList<SkillDefinition> GetInstalledSkills(ProjectSkillsManifest manifest)
        {
            var installedIds = new HashSet<string>(
                GetBuiltInSkills().Select(skill => skill.Id),
                StringComparer.OrdinalIgnoreCase);

            if (manifest?.optionalSkills != null)
            {
                foreach (var id in manifest.optionalSkills)
                    installedIds.Add(id);
            }

            return SkillCatalog.Where(skill => installedIds.Contains(skill.Id)).ToArray();
        }

        internal static bool IsManagedFile(string path)
        {
            try
            {
                return File.Exists(path) && File.ReadAllText(path).Contains(ManagedMarker, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        internal static string[] GetPlatformConflictPaths(string projectRoot, IEnumerable<string> selectedPlatforms)
        {
            var conflicts = new List<string>();
            var platforms = new HashSet<string>(selectedPlatforms ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            if (platforms.Contains("codex"))
            {
                var path = GetCodexAgentsPath(projectRoot);
                if (File.Exists(path) && !IsManagedFile(path))
                    conflicts.Add(path);
            }

            if (platforms.Contains("claude"))
            {
                var path = GetClaudeInstructionsPath(projectRoot);
                if (File.Exists(path) && !IsManagedFile(path))
                    conflicts.Add(path);
            }

            return conflicts.ToArray();
        }

        internal static IReadOnlyList<string> GetGeneratedPathsForPlatform(string projectRoot, ProjectSkillsManifest manifest, string platformId)
        {
            var paths = new List<string> { GetManifestPath(projectRoot) };
            var enabled = manifest != null && manifest.platforms.Contains(platformId, StringComparer.OrdinalIgnoreCase);
            if (!enabled)
                return paths;

            switch (platformId?.Trim().ToLowerInvariant())
            {
                case "codex":
                    paths.Add(GetCodexAgentsPath(projectRoot));
                    paths.Add(GetCodexSkillsRoot(projectRoot));
                    break;
                case "claude":
                    paths.Add(GetClaudeInstructionsPath(projectRoot));
                    paths.Add(GetClaudeCommandsRoot(projectRoot));
                    break;
                case "cursor":
                    paths.Add(GetCursorRulesPath(projectRoot));
                    break;
            }

            return paths;
        }

        private static void SyncCodex(string projectRoot, ProjectSkillsManifest manifest)
        {
            var enabled = manifest.platforms.Contains("codex", StringComparer.OrdinalIgnoreCase);
            var agentsPath = GetCodexAgentsPath(projectRoot);
            var skillsRoot = GetCodexSkillsRoot(projectRoot);

            if (!enabled)
            {
                DeleteManagedFile(agentsPath);
                DeleteManagedSkillDirectories(skillsRoot);
                return;
            }

            Directory.CreateDirectory(skillsRoot);

            File.WriteAllText(agentsPath, BuildCodexAgentsContent(projectRoot, manifest));
            WriteManagedSkillDirectories(skillsRoot, manifest, SkillPlatform.Codex);
        }

        private static void SyncClaude(string projectRoot, ProjectSkillsManifest manifest)
        {
            var enabled = manifest.platforms.Contains("claude", StringComparer.OrdinalIgnoreCase);
            var claudePath = GetClaudeInstructionsPath(projectRoot);
            var commandsRoot = GetClaudeCommandsRoot(projectRoot);

            if (!enabled)
            {
                DeleteManagedFile(claudePath);
                DeleteManagedCommandFiles(commandsRoot);
                return;
            }

            Directory.CreateDirectory(commandsRoot);

            File.WriteAllText(claudePath, BuildClaudeInstructionsContent(projectRoot, manifest));
            WriteManagedClaudeCommands(commandsRoot, manifest);
        }

        private static void SyncCursor(string projectRoot, ProjectSkillsManifest manifest)
        {
            var enabled = manifest.platforms.Contains("cursor", StringComparer.OrdinalIgnoreCase);
            var rulesRoot = GetCursorRulesPath(projectRoot);

            if (!enabled)
            {
                DeleteManagedCursorRules(rulesRoot);
                return;
            }

            Directory.CreateDirectory(rulesRoot);
            WriteManagedCursorRules(rulesRoot, manifest);
        }

        private static void WriteManagedSkillDirectories(string skillsRoot, ProjectSkillsManifest manifest, SkillPlatform platform)
        {
            DeleteManagedSkillDirectories(skillsRoot);

            foreach (var skill in GetInstalledSkills(manifest))
            {
                var directory = Path.Combine(skillsRoot, $"funplay-{skill.Id}");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "SKILL.md"), BuildSkillDocument(skill, platform));
            }
        }

        private static void WriteManagedClaudeCommands(string commandsRoot, ProjectSkillsManifest manifest)
        {
            DeleteManagedCommandFiles(commandsRoot);

            foreach (var skill in GetInstalledSkills(manifest))
            {
                var path = Path.Combine(commandsRoot, $"funplay-{skill.Id}.md");
                File.WriteAllText(path, BuildClaudeCommandContent(skill));
            }
        }

        private static void WriteManagedCursorRules(string rulesRoot, ProjectSkillsManifest manifest)
        {
            DeleteManagedCursorRules(rulesRoot);

            foreach (var skill in GetInstalledSkills(manifest))
            {
                var path = Path.Combine(rulesRoot, $"funplay-{skill.Id}.mdc");
                File.WriteAllText(path, BuildCursorRuleContent(skill));
            }
        }

        private static void DeleteManagedSkillDirectories(string skillsRoot)
        {
            if (!Directory.Exists(skillsRoot))
                return;

            foreach (var directory in Directory.GetDirectories(skillsRoot, "funplay-*", SearchOption.TopDirectoryOnly))
            {
                var skillPath = Path.Combine(directory, "SKILL.md");
                if (IsManagedFile(skillPath))
                    Directory.Delete(directory, true);
            }
        }

        private static void DeleteManagedCommandFiles(string commandsRoot)
        {
            if (!Directory.Exists(commandsRoot))
                return;

            foreach (var file in Directory.GetFiles(commandsRoot, "funplay-*.md", SearchOption.TopDirectoryOnly))
            {
                if (IsManagedFile(file))
                    File.Delete(file);
            }
        }

        private static void DeleteManagedCursorRules(string rulesRoot)
        {
            if (!Directory.Exists(rulesRoot))
                return;

            foreach (var file in Directory.GetFiles(rulesRoot, "funplay-*.mdc", SearchOption.TopDirectoryOnly))
            {
                if (IsManagedFile(file))
                    File.Delete(file);
            }
        }

        private static void DeleteManagedFile(string path)
        {
            if (IsManagedFile(path))
                File.Delete(path);
        }

        private static string BuildCodexAgentsContent(string projectRoot, ProjectSkillsManifest manifest)
        {
            var installed = GetInstalledSkills(manifest);
            return
$@"# AGENTS.md
{ManagedMarker}

# Funplay Unity MCP Project Guidance

This file is managed by Funplay MCP for Unity.

## Installed project skills

{string.Join("\n", installed.Select(skill => $"- `funplay-{skill.Id}` - {skill.Description}"))}

## Codex workflow rules

- Prefer project-local Funplay skills under `.agents/skills/`.
- Use `execute_code` as the primary Unity automation tool.
- Call `request_recompile` immediately after editing scripts or `Assets/` files outside Unity.
- If recompilation triggers a domain reload, call `get_reload_recovery_status`.
- Avoid changing `Library/`, `Temp/`, `Logs/`, or `obj/`.

## Project

- Project root: `{projectRoot}`
- Product name: `{Application.productName}`

## Notes

- Re-run `Funplay > MCP Server > Configure Skills` after changing selected skills or platforms.
";
        }

        private static string BuildClaudeInstructionsContent(string projectRoot, ProjectSkillsManifest manifest)
        {
            var installed = GetInstalledSkills(manifest);
            return
$@"# CLAUDE.md
{ManagedMarker}

# Funplay Unity MCP Project Guidance

This file is managed by Funplay MCP for Unity for Claude Code.

## Installed skills

{string.Join("\n", installed.Select(skill => $"- `{skill.Id}` - {skill.Description}"))}

## Preferred workflow

- Use Funplay MCP tools for Unity editor state and automation.
- Use `execute_code` for non-trivial Unity orchestration.
- Call `request_recompile` after external script edits before assuming Unity imported the latest code.
- If domain reload interrupts a request, follow with `get_reload_recovery_status`.
- Additional installed skills are available as project commands under `.claude/commands/`.

## Project

- Project root: `{projectRoot}`
- Product name: `{Application.productName}`
";
        }

        private static string BuildClaudeCommandContent(SkillDefinition skill)
        {
            return
$@"{ManagedMarker}

# {skill.Title}

{skill.WhenToUse}

## Rules

{string.Join("\n", skill.Rules.Select(rule => $"- {rule}"))}

## Metadata

- Skill id: `{skill.Id}`
- Source: `https://github.com/FunplayAI/funplay-skill`
";
        }

        private static string BuildCursorRuleContent(SkillDefinition skill)
        {
            var alwaysApply = skill.IsBuiltIn ? "true" : "false";
            return
$@"---
description: {skill.Description}
alwaysApply: {alwaysApply}
---
{ManagedMarker}

# {skill.Title}

{skill.WhenToUse}

## Rules

{string.Join("\n", skill.Rules.Select(rule => $"- {rule}"))}

## Metadata

- Skill id: `{skill.Id}`
- Built-in: `{skill.IsBuiltIn}`
- Source: `https://github.com/FunplayAI/funplay-skill`
";
        }

        private static string BuildSkillDocument(SkillDefinition skill, SkillPlatform platform)
        {
            return
$@"---
name: funplay-{skill.Id}
description: {skill.Description}
platform: {platform.ToString().ToLowerInvariant()}
---
{ManagedMarker}

# {skill.Title}

{skill.WhenToUse}

## Rules

{string.Join("\n", skill.Rules.Select(rule => $"- {rule}"))}

## Metadata

- Original skill id: `{skill.Id}`
- Source repository: `https://github.com/FunplayAI/funplay-skill`
";
        }

        private static ProjectSkillsManifest CreateDefaultManifest()
        {
            return new ProjectSkillsManifest
            {
                platforms = new List<string>(),
                optionalSkills = new List<string>()
            };
        }

        private static ProjectSkillsManifest NormalizeManifest(ProjectSkillsManifest manifest)
        {
            manifest ??= CreateDefaultManifest();
            manifest.platforms ??= new List<string>();
            manifest.optionalSkills ??= new List<string>();

            manifest.platforms = manifest.platforms
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim().ToLowerInvariant())
                .Where(value => SupportedPlatforms.Contains(value, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var optionalIds = new HashSet<string>(
                GetOptionalSkills().Select(skill => skill.Id),
                StringComparer.OrdinalIgnoreCase);

            manifest.optionalSkills = manifest.optionalSkills
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Where(value => optionalIds.Contains(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return manifest;
        }

        internal enum SkillPlatform
        {
            Codex,
            Claude,
            Cursor
        }

        [Serializable]
        internal sealed class ProjectSkillsManifest
        {
            public List<string> platforms = new List<string>();
            public List<string> optionalSkills = new List<string>();
        }

        internal sealed class SkillDefinition
        {
            public SkillDefinition(string id, string title, string description, bool isBuiltIn, string whenToUse, IReadOnlyList<string> rules)
            {
                Id = id;
                Title = title;
                Description = description;
                IsBuiltIn = isBuiltIn;
                WhenToUse = whenToUse;
                Rules = rules ?? Array.Empty<string>();
            }

            public string Id { get; }
            public string Title { get; }
            public string Description { get; }
            public bool IsBuiltIn { get; }
            public string WhenToUse { get; }
            public IReadOnlyList<string> Rules { get; }
        }
    }
}
