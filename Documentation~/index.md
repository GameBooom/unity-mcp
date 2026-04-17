# Funplay MCP for Unity

Funplay MCP for Unity is an open-source MCP server for the Unity Editor.

## Getting Started

1. Install via UPM using the Git URL for this repository
2. Open **Funplay > MCP Server**
3. Start the server and use the built-in one-click client configuration
4. Connect your AI client to the endpoint shown in the window (`http://127.0.0.1:8765/` by default)
5. Optionally open **Funplay > Project Skills (Experimental)** to configure built-in and optional skills for supported AI clients

## Highlights

- 79 built-in tool functions across scene, asset, script, prefab, UI, animation, camera, screenshot, package, and feedback workflows
- HTTP JSON-RPC 2.0 MCP server compatible with Claude Code, Cursor, Windsurf, Codex, VS Code Copilot, and other MCP clients
- Reflection-based tool discovery via `[ToolProvider]`
- One-click local MCP config generation for supported clients
- Experimental project skills management for supported AI clients, with built-in and optional skills
- Persisted MCP server settings in `UserSettings/FunplayMcpSettings.json`
- Domain reload recovery for the MCP server during Unity recompilation

## Custom Tools

Add a public static class marked with `[ToolProvider("CategoryName")]`, then expose `public static string` methods with `[ToolParam]` metadata. Tool names are exported in snake_case automatically.

## Requirements

- Unity 2022.3 or later
- `com.unity.nuget.newtonsoft-json`
