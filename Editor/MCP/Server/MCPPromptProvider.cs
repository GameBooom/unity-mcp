// Copyright (C) GameBooom. Licensed under MIT.

using System.Collections.Generic;

namespace GameBooom.Editor.MCP.Server
{
    internal class MCPPromptProvider
    {
        private readonly string _projectName;
        private readonly string _projectPath;

        public MCPPromptProvider(string projectName, string projectPath)
        {
            _projectName = string.IsNullOrEmpty(projectName) ? "Unity Project" : projectName;
            _projectPath = string.IsNullOrEmpty(projectPath) ? "Unknown" : projectPath;
        }

        public List<Dictionary<string, object>> ListPrompts()
        {
            return new List<Dictionary<string, object>>
            {
                CreatePrompt("fix_compile_errors", $"Diagnose and repair current Unity compilation failures in project '{_projectName}'."),
                CreatePrompt("create_playable_prototype", $"Build a playable Unity prototype in project '{_projectName}' from a short idea."),
                CreatePrompt("runtime_validation", $"Validate a gameplay change in Play Mode for project '{_projectName}' with checks and logs."),
                CreatePrompt("auto_wire_references", $"Repair missing serialized references on a selected setup in project '{_projectName}'.")
            };
        }

        public Dictionary<string, object> GetPrompt(string name, Dictionary<string, object> arguments)
        {
            string text;

            switch (name)
            {
                case "fix_compile_errors":
                    text = "Inspect active compile errors, patch the smallest safe regions, recompile, and verify no errors remain.";
                    break;
                case "create_playable_prototype":
                    text = "Create a playable Unity prototype from the provided idea. Build the environment, input, camera, helper UI, and verify the result.";
                    break;
                case "runtime_validation":
                    text = "Enter Play Mode if needed, wait for simulation to advance, run targeted validation checks, and summarize the runtime outcome with console errors.";
                    break;
                case "auto_wire_references":
                    text = "Inspect the target component, fill missing Unity object references with the best scene or asset matches, then verify the wired state.";
                    break;
                default:
                    text = "Prompt not found: " + name;
                    break;
            }

            text = $"Target Unity project: {_projectName}\nProject path: {_projectPath}\n\n{text}";

            return new Dictionary<string, object>
            {
                ["description"] = text,
                ["messages"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["role"] = "user",
                        ["content"] = new Dictionary<string, object>
                        {
                            ["type"] = "text",
                            ["text"] = text
                        }
                    }
                }
            };
        }

        private static Dictionary<string, object> CreatePrompt(string name, string description)
        {
            return new Dictionary<string, object>
            {
                ["name"] = name,
                ["description"] = description,
                ["arguments"] = new List<object>()
            };
        }
    }
}
