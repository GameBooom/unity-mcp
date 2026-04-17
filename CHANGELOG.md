# Changelog

## [0.1.10] - 2026-04-17

### Added
- Added `Funplay > Project Skills (Experimental)` as a dedicated window for project-level skills setup
- Added built-in and optional project skills management for supported AI clients, with per-platform generated file visibility
- Added persistence for the currently selected one-click configuration target so related tools stay aligned across sessions

### Changed
- Moved project skills management out of the MCP Server window into its own dedicated menu entry
- Improved the Project Skills window layout with clearer sections and installed-file visibility
- Removed automatic port fallback so the MCP server now starts only on the configured port
- Replaced Unity editor star-prompt emoji with plain text for better font compatibility across Unity versions

## [0.1.9] - 2026-04-16

### Fixed
- Fixed one-click MCP configuration paths on Windows by resolving the real user profile directory
- Fixed VS Code one-click configuration to use the platform-specific user config directory with a macOS fallback
- Ensured one-click MCP configuration writes the currently running server port after automatic port fallback

## [0.1.8] - 2026-04-15

### Changed
- Rebranded the open-source package and documentation from GameBooom to Funplay
- Moved the public Git repository to `FunplayAI/funplay-unity-mcp`
- Updated Unity menu paths to `Funplay/MCP Server` and `Funplay/Check for Updates`
- Reorganized the README quick start and one-click client configuration guidance

## [0.1.7] - 2026-04-10

### Changed
- Repurposed `request_recompile` into the default AI-facing sync flow for external file edits, compilation, and domain reload recovery
- Removed `sync_external_changes` from the exposed MCP tool list to avoid duplicate AI pathways
- Prevented MCP transport restarts from running on a background thread after settings changes
- Avoided redundant settings change notifications and UI initialization callbacks in the MCP Server window

## [0.1.6] - 2026-04-08

### Added
- Updated `request_recompile` to import external file edits and wait through compilation/domain reload recovery

### Changed
- Strengthened `request_recompile` tool guidance so AI clients treat it as the default follow-up after external file edits
- Improved `request_recompile` behavior to return an explicit compilation/reload message instead of failing ambiguously during domain reload
- Persist and report recovery results for external sync operations through `get_reload_recovery_status`

## [0.1.5] - 2026-04-01

### Added
- Performance analysis tools: `get_performance_snapshot` and `analyze_scene_complexity`

### Changed
- Core MCP tool profile now includes lightweight performance inspection by default

## [0.1.4] - 2026-04-01

### Added
- Built-in update checking from `Funplay/Check for Updates` with install-source aware behavior
- Automatic Git package refresh for Git-based installs
- Automatic latest `.unitypackage` download and import for asset-import installs

### Changed
- Game View screenshots now default to the current Game View render size instead of a fixed 512x512 capture
- Mouse click simulation now maps coordinates against the real Game View render size for more reliable UI and physics hits
- Package version resolution now prefers the actual installed package location so Git installs report the correct version
- Package metadata now points to the `FunplayAI/funplay-unity-mcp` repository and `0.1.4`

## [0.1.2] - 2026-03-30

### Added
- MCP prompts support with `prompts/list` and `prompts/get`
- Rich MCP resources with project context, scene/selection/error summaries, interaction history, and resource templates
- `execute_code` as the primary high-flexibility orchestration tool
- Input simulation tools for key press, key combo, mouse click, and mouse drag workflows
- Lightweight editor context builder and package version resolver for richer MCP context output

### Changed
- Default MCP tool exposure now uses a `core` profile to reduce tool-list noise, with optional `full` exposure in the MCP Server window
- Tools exposed by the open-source build now execute directly without an extra approval toggle
- Play Mode MCP requests no longer stall on the editor thread dispatch path
- MCP server info now reports the package version dynamically instead of a hard-coded version

## [0.1.1] - 2026-03-19

### Added
- Minimal MCP resources support with `resources/list`, `resources/read`, and project/scene resource endpoints
- Reload recovery reporting via `get_reload_recovery_status`
- Cached Unity console log access via `get_console_logs`

### Changed
- Bind and document the default local MCP endpoint as `http://127.0.0.1:8765/` for better Codex compatibility
- Auto-start the MCP server on editor load when it is enabled in settings
- Improve compilation tracking and persist interrupted tool execution across domain reloads

## [0.1.0] - 2026-03-12

### Added
- Initial release of Funplay MCP for Unity (Community Edition)
- MCP Server with HTTP JSON-RPC 2.0 transport
- 60+ built-in tool functions across 15 modules (scene, asset, script, UI, camera, animation, etc.)
- Reflection-based tool discovery with attribute annotations
- Custom tool support via `[ToolProvider]` attribute
- MCP Client for connecting to external MCP servers
- One-click MCP config generation for Claude Code, Cursor, VS Code, Trae, Kiro, and Codex
- Domain reload survival across Unity recompilations
- UPM package distribution via Git URL
