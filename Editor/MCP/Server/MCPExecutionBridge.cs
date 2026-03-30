// Copyright (C) GameBooom. Licensed under MIT.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GameBooom.Editor.Settings;
using GameBooom.Editor.State;
using GameBooom.Editor.Threading;
using GameBooom.Editor.Tools;
using UnityEngine;

namespace GameBooom.Editor.MCP.Server
{
    /// <summary>
    /// Bridges MCP tool calls to GameBooom's FunctionInvokerController.
    /// Handles thread marshalling and approval workflow.
    /// </summary>
    internal class MCPExecutionBridge
    {
        private readonly IEditorThreadHelper _threadHelper;
        private readonly ISettingsController _settings;
        private readonly IStateController _stateController;
        private readonly FunctionInvokerController _invoker;
        private readonly MCPInteractionLog _interactionLog;

        public MCPExecutionBridge(
            IEditorThreadHelper threadHelper,
            ISettingsController settings,
            IStateController stateController,
            FunctionInvokerController invoker,
            MCPInteractionLog interactionLog)
        {
            _threadHelper = threadHelper ?? throw new ArgumentNullException(nameof(threadHelper));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _stateController = stateController ?? throw new ArgumentNullException(nameof(stateController));
            _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
            _interactionLog = interactionLog;
        }

        public async Task<string> ExecuteToolAsync(
            string toolName,
            Dictionary<string, object> arguments,
            CancellationToken ct)
        {
            return await _threadHelper.ExecuteAsyncOnEditorThreadAsync(async () =>
            {
                try
                {
                    var functionCall = new FunctionCall
                    {
                        Id = Guid.NewGuid().ToString(),
                        FunctionName = toolName
                    };

                    foreach (var kvp in arguments)
                        functionCall.Parameters[kvp.Key] = ConvertArgumentToString(kvp.Value);

                    ToolRegistry.ManualTools.TryGetValue(toolName, out var manualTool);
                    var method = ToolRegistry.GetMethod(toolName);
                    if (method == null && manualTool == null)
                    {
                        return $"Error: Unknown tool '{toolName}'";
                    }

                    var profile = MCPToolExportPolicy.Parse(_settings.MCPToolExportProfile);
                    if (!MCPToolExportPolicy.IsToolAllowed(toolName, profile))
                    {
                        return $"Error: Tool '{toolName}' is not exposed by the current MCP tool profile '{MCPToolExportPolicy.ToSettingValue(profile)}'.";
                    }

                    functionCall.IsReadOnly = method != null &&
                        method.GetCustomAttribute<ReadOnlyToolAttribute>() != null;

                    DomainReloadHandler.ResetResumeCounter();
                    _stateController.SetState(GameBooomState.ExecutingFunction);
                    DomainReloadHandler.SavePendingFunction(functionCall);

                    Debug.Log($"[GameBooom MCP Server] Executing tool: {toolName}");
                    var result = await _invoker.InvokeAsync(functionCall);
                    DomainReloadHandler.ClearPendingFunction();
                    _stateController.ReturnToPreviousState();

                    if (!string.IsNullOrEmpty(functionCall.Error))
                    {
                        var errMsg = $"Error: {functionCall.Error}";
                        _interactionLog?.Add(toolName, MCPToolCallStatus.Error, errMsg);
                        return errMsg;
                    }

                    var resultText = result ?? "Completed successfully";
                    _interactionLog?.Add(toolName, MCPToolCallStatus.Success, resultText);
                    return resultText;
                }
                catch (Exception ex)
                {
                    DomainReloadHandler.ClearPendingFunction();
                    _stateController.ClearState();
                    var exError = $"Error: {ex.Message}";
                    Debug.LogError($"[GameBooom MCP Server] Error executing tool '{toolName}': {ex.Message}\n{ex.StackTrace}");
                    _interactionLog?.Add(toolName, MCPToolCallStatus.Error, exError);
                    return exError;
                }
            });
        }

        private string ConvertArgumentToString(object value)
        {
            if (value == null) return string.Empty;
            if (value is string strValue) return UnescapeXmlEntities(strValue);
            if (value is bool boolValue) return boolValue ? "true" : "false";
            if (value is int || value is long || value is float || value is double) return value.ToString();
            if (value is Dictionary<string, object> dict) return SimpleJsonHelper.Serialize(dict);
            if (value is System.Collections.IList list)
            {
                var items = new List<object>();
                foreach (var item in list) items.Add(item);
                return SimpleJsonHelper.Serialize(items);
            }
            return value.ToString();
        }

        private string UnescapeXmlEntities(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            return str
                .Replace("&lt;", "<")
                .Replace("&gt;", ">")
                .Replace("&amp;", "&")
                .Replace("&quot;", "\"")
                .Replace("&apos;", "'");
        }
    }
}
