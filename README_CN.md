<p align="center">
  <h1 align="center">Funplay MCP for Unity</h1>
  <p align="center">
    <strong>The Most Advanced MCP Server for Unity Editor</strong>
  </p>
  <p align="center">
    <a href="#"><img src="https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity" alt="Unity 6000.0+"></a>
    <a href="#"><img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="License: MIT"></a>
    <a href="#"><img src="https://img.shields.io/badge/MCP-Compatible-green" alt="MCP Compatible"></a>
    <a href="#"><img src="https://img.shields.io/badge/Platform-Editor%20Only-orange" alt="Editor Only"></a>
  </p>
  <p align="center">
    中文 | <a href="./README.md">English</a>
  </p>
  <p align="center">
    <img src="./Documentation~/Text%2BLogo.png" alt="The Most Advanced MCP Server for Unity" width="100%">
  </p>
</p>

> 💖 如果这个项目对你有帮助，欢迎顺手点一个 Star。它能帮助更多 Unity 开发者发现这个项目，也能支持后续持续维护。

---

Funplay MCP for Unity 是一个采用 MIT 协议的 Unity 编辑器 MCP 服务器，让 Claude Code、Cursor、Windsurf、Codex、VS Code Copilot 等 AI 助手直接操作正在运行的 Unity 项目。

一句话描述你的游戏 — AI 助手通过 Funplay MCP for Unity 的 79 个内置工具自动创建场景、编写脚本、验证运行态、模拟输入、分析性能并完成编辑器自动化，把所有逻辑串联起来。

> *"做一个贪吃蛇游戏，10x10 网格，食物随机生成，计分 UI，游戏结束界面"*
>
> AI 助手通过 Funplay MCP for Unity 全程处理：创建场景、生成全部脚本、搭建 UI、配置游戏逻辑 — 只需一句话。

## 快速开始

如果你只想尽快跑起来，先做这三步：

- 用 Git URL 安装 Unity 包
- 打开 `Funplay > MCP Server`
- 使用内置的一键客户端配置

### 1. 通过 UPM 安装 (Git URL)

在 Unity 中，打开 **Window → Package Manager → + → Add package from git URL**：

```
https://github.com/FunplayAI/funplay-unity-mcp.git
```

> 💡 在 clone 或安装之前，如果你愿意顺手点一个 ⭐，会非常感谢。

### 2. 启动 MCP Server

**菜单：Funplay → MCP Server** 启动服务。

默认从 `http://127.0.0.1:8765/` 启动。

如果你想编辑 `core` 或 `full` 各自暴露哪些工具，可以打开 **Funplay → Tool Exposure**。

排查问题时如果需要调整插件 debug 日志，可以打开 **Funplay → Plugin Settings**。

### 3. 配置 AI 客户端

优先使用 `Funplay > MCP Server` 窗口里的 **一键 MCP 配置**。

选择目标客户端后点击 **Configure**，插件会直接帮你写入推荐的 MCP 配置项。

对于 Claude Code、Cursor 和 Codex，也可以点击 **Configure + Skills**，同时安装默认的项目级 MCP 工作流 skill。

如果你希望为当前 Unity 项目配置项目级 AI 指引，可以打开 **Funplay → Project Skills**，为支持的平台安装默认的 `unity-mcp-workflow` skill。

如果你更想手动编辑配置文件，再参考下面这些示例：

<details>
<summary>Claude Code / Claude Desktop</summary>

```json
{
  "mcpServers": {
    "funplay": {
      "type": "http",
      "url": "http://127.0.0.1:8765/"
    }
  }
}
```

</details>

<details>
<summary>Cursor</summary>

```json
{
  "mcpServers": {
    "funplay": {
      "url": "http://127.0.0.1:8765/"
    }
  }
}
```

</details>

<details>
<summary>VS Code</summary>

```json
{
  "servers": {
    "funplay": {
      "type": "http",
      "url": "http://127.0.0.1:8765/"
    }
  }
}
```

</details>

<details>
<summary>Trae</summary>

```json
{
  "mcpServers": {
    "funplay": {
      "url": "http://127.0.0.1:8765/"
    }
  }
}
```

</details>

<details>
<summary>Kiro</summary>

```json
{
  "mcpServers": {
    "funplay": {
      "type": "http",
      "url": "http://127.0.0.1:8765/"
    }
  }
}
```

</details>

<details>
<summary>Codex</summary>

```toml
[mcp_servers.funplay]
url = "http://127.0.0.1:8765/"
```

</details>

<details>
<summary>Windsurf</summary>

除非你本地 Windsurf 版本要求不同的 MCP 配置格式，否则可直接使用与 Cursor 相同的 JSON 结构。

</details>

### 4. 验证连接

先在 AI 客户端里试几个安全请求：

> “调用 `get_scene_info`，告诉我当前打开的是哪个场景。”

> “读取 `unity://project/context`，总结当前编辑器状态。”

> “调用 `execute_code`，返回当前激活场景名。”

如果这些都正常返回，说明 MCP server、resources 和主执行工具都已经连通。

### 5. 开始构建

打开你的 AI 客户端，试试：*"创建一个 3D 平台跳跃关卡，包含 5 个浮空平台"*

## 开始前说明

- 这是一个 **仅限 Editor** 的包，不会向最终构建产物添加运行时代码。
- MCP Server 默认从 `http://127.0.0.1:8765/` 启动。
- 本地 MCP Server 配置保存在 `UserSettings/FunplayMcpSettings.json`。
- 插件默认使用 `core` MCP 工具暴露配置，减少 AI 客户端的工具噪音；`core` 当前暴露 19 个高频工具，以 `execute_code`、运行模式控制、输入模拟、截图、性能检查、日志和编译检查为主。如果你需要完整工具集，可在 MCP Server 窗口切换到 `full`，暴露全部 79 个工具。
- 插件 debug 日志默认开启，可在 **Funplay > Plugin Settings** 中关闭；Warning 和 Error 始终会输出到 Unity Console。
- 所有已暴露的 MCP 工具都会直接执行，不再提供额外的 approval 开关。
- **菜单：`Funplay > Check for Updates`** 可按安装来源自动更新：Git 安装会直接重新拉取，`.unitypackage` 导入会自动下载并导入最新版。

## 能力概览

- **`execute_code` 主工具优先** — 核心体验围绕一个高灵活度 C# 执行工具构建，适合复杂编辑器/运行态编排
- **Play Mode 自动化闭环** — 进入运行模式、模拟键鼠输入、截图、查看日志、验证行为都能在同一 MCP 会话里完成
- **内建项目上下文** — 直接提供项目状态、当前场景、选择对象、编译错误、控制台输出和 MCP 交互记录资源
- **默认聚焦，必要时全量** — 默认 `core` 工具集更利于 AI 选工具，需要时可切到 `full` 暴露全部 79 个工具
- **单 Unity 包落地** — 不需要额外 approval 开关，Unity 侧也不依赖单独 Python 守护进程
- **可扩展** — 支持 Attribute 发现自定义工具，也支持连接外部 MCP 服务

## 核心特性

- **79 个内置工具** — 覆盖场景编辑、脚本、资产、运行态控制、截图、性能分析、Prompts、Resources 与编辑器自动化，共 19 个模块
- **Resources 与 Prompts** — 暴露实时项目上下文、场景/选择/错误资源、资源模板，以及常见 Unity 工作流的可复用 MCP Prompt
- **输入模拟 + 截图验证** — 在 Play Mode 中模拟键盘/鼠标，再用 Game View / Scene View 截图验证结果
- **内置更新** — 直接在 Unity 菜单中检查更新，并根据安装方式自动重新拉取 Git 包或导入最新 `unitypackage`
- **一键客户端配置** — 直接在 Unity 窗口里为 Claude Code、Cursor、VS Code、Kiro、Trae、Codex 等客户端生成 MCP 配置
- **工具暴露控制** — 编辑 `core` 和 `full` 各自暴露的具体工具
- **项目 Skills 管理器** — 为支持的 AI 客户端配置项目级 skills，目前安装默认的 `unity-mcp-workflow` skill
- **插件设置** — 排查 MCP 连接或工具执行问题时，可开关详细 debug 日志
- **厂商无关** — 兼容任意支持 MCP 的 AI 客户端：Claude Code、Cursor、Windsurf、Codex、VS Code Copilot 等

## 与 Coplay 的对比

下表基于 Coplay 官方公开 GitHub README 所描述的能力与安装方式进行对比。

| 维度 | Funplay MCP for Unity | Coplay `unity-mcp` |
|------|-------------------------|--------------------|
| Unity 侧架构 | Unity 包内置 HTTP MCP server | Unity bridge + 本地 Python MCP server |
| 额外本地依赖 | `core` 工作流下只需要 Unity 包本身 | 官方 quick start 要求 Python 3.10+ 与 `uv` |
| 主要交互模型 | 以 `execute_code` 为主，再配合少量高频辅助工具 | 以大量 `manage_*` 工具族为主 |
| 默认工具暴露 | 默认 `core` 精简工具集，可切 `full` | 公开文档强调广泛工具面 |
| 上下文能力 | 内建项目资源、资源模板、工作流 prompts、交互历史 | 公开 README 主要强调 bridge/server 与工具族 |
| Play Mode 验证 | 包内置运行模式控制、截图、日志、输入模拟 | 公开 README 强调广泛 Unity 管理与自动化能力 |
| 定位 | 轻量、直接、MIT 协议的 Unity MCP 服务器 | Coplay 维护的全功能 Unity bridge 方案 |

Coplay 信息来源：[CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp)

## MCP 能力结构

当前开源包有四层高价值能力：

- **Tools** — `full` 下共 79 个工具，`core` 下 19 个高频工具
- **Primary execution** — `execute_code` 用于复杂编辑器/运行态编排
- **Prompts** — 包括 `fix_compile_errors`、`runtime_validation`、`create_playable_prototype` 等工作流 Prompt
- **Resources** — 项目上下文、场景摘要、选择状态、编译错误、控制台错误、MCP 交互记录，以及按对象/组件/资源路径展开的模板资源

## 内置工具

Funplay MCP for Unity 当前提供 **79 个工具函数**，覆盖 19 个模块：

| 分类 | 工具 |
|------|------|
| **游戏对象** | `create_primitive`, `create_game_object`, `delete_game_object`, `find_game_objects`, `get_game_object_info`, `set_transform`, `duplicate_game_object`, `rename_game_object`, `set_parent`, `add_component`, `set_tag_and_layer`, `set_active` |
| **层级** | `get_hierarchy` |
| **组件** | `get_component_properties`, `list_components`, `set_component_property`, `set_component_properties` |
| **脚本** | `create_script`, `edit_script`, `patch_script` |
| **资产** | `create_material`, `assign_material`, `find_assets`, `delete_asset`, `rename_asset`, `copy_asset` |
| **文件** | `read_file`, `write_file`, `search_files`, `list_directory`, `exists` |
| **场景** | `get_scene_info`, `list_scenes`, `save_scene`, `open_scene`, `create_new_scene`, `enter_play_mode`, `exit_play_mode`, `set_time_scale`, `get_time_scale` |
| **预制体** | `create_prefab`, `instantiate_prefab`, `unpack_prefab` |
| **UI** | `create_canvas`, `create_button`, `create_text`, `create_image` |
| **动画** | `create_animation_clip`, `create_animator_controller`, `assign_animator` |
| **相机** | `get_camera_properties`, `set_camera_projection`, `set_camera_settings`, `set_camera_culling_mask` |
| **截图** | `capture_game_view`, `capture_scene_view` |
| **脚本执行** | `execute_code` |
| **输入模拟** | `simulate_key_press`, `simulate_key_combo`, `simulate_mouse_click`, `simulate_mouse_drag` |
| **性能分析** | `get_performance_snapshot`, `analyze_scene_complexity` |
| **包管理** | `install_package`, `remove_package`, `list_packages` |
| **编译** | `wait_for_compilation`, `request_recompile`, `get_compilation_errors`, `get_reload_recovery_status` |
| **可视化反馈** | `select_object`, `focus_on_object`, `ping_asset`, `log_message`, `show_dialog`, `get_console_logs` |

## 添加自定义工具

通过简单的 Attribute 标注即可创建自定义工具：

```csharp
using System.ComponentModel;

[ToolProvider("MyTools")]
public static class MyCustomTools
{
    [Description("Spawns enemies at random positions in the scene")]
    public static string SpawnEnemies(
        [ToolParam("Number of enemies to spawn", Required = true)] int count,
        [ToolParam("Prefab path in Assets")] string prefabPath)
    {
        // Your implementation here
        return $"Spawned {count} enemies";
    }
}
```

方法会被自动发现，名称转换为 snake_case（`spawn_enemies`），并通过 MCP 自动生成 JSON Schema 定义暴露给 AI。

## 架构

```
MCP Server (HTTP JSON-RPC 2.0)
    └─ MCPRequestHandler (协议处理)
        └─ MCPExecutionBridge
            └─ FunctionInvokerController (反射式调用)
                └─ Tool Functions (79 个内置工具，19 个模块)
```

```
外部 AI 客户端 → HTTP 请求 → MCPRequestHandler → MCPExecutionBridge → FunctionInvokerController → 工具方法
```

## 环境要求

- Unity 2022.3 或更高版本
- .NET / Mono + `Newtonsoft.Json`

## 参与贡献

欢迎贡献！提交 PR 前请阅读 [贡献指南](CONTRIBUTING.md)。

## 许可证

[MIT](LICENSE) — 可自由使用、修改、分发，也可集成到商业或开源项目中。
