// Copyright (C) GameBooom. Licensed under MIT.

using System;
using System.Threading;
using System.Threading.Tasks;
using GameBooom.Editor.MCP;
using GameBooom.Editor.Services;
using GameBooom.Editor.Settings;
using GameBooom.Editor.State;
using GameBooom.Editor.Threading;
using GameBooom.Editor.Tools;
using UnityEditor;
using UnityEngine;

namespace GameBooom.Editor.MCP.Server
{
    /// <summary>
    /// Main MCP server service singleton.
    /// Manages server lifecycle, coordinates transport, handler, exporter, and bridge.
    /// </summary>
    internal class MCPServerService : IDisposable
    {
        private readonly ISettingsController _settings;
        private readonly IEditorThreadHelper _threadHelper;
        private readonly IStateController _stateController;
        private readonly IEditorContextBuilder _contextBuilder;
        private readonly IApplicationPaths _applicationPaths;
        private readonly FunctionInvokerController _invoker;

        private IMCPTransport _transport;
        private MCPRequestHandler _requestHandler;
        private MCPResourceProvider _resourceProvider;
        private bool _isRunning;
        private bool _disposed;
        private bool _recoveryChecked;
        private string _toolExportProfileSetting;

        public bool IsRunning => _isRunning;
        public int Port { get; private set; }
        public MCPInteractionLog InteractionLog { get; }

        public MCPServerService(
            ISettingsController settings,
            IEditorThreadHelper threadHelper,
            IStateController stateController,
            IEditorContextBuilder contextBuilder,
            IApplicationPaths applicationPaths,
            FunctionInvokerController invoker)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _threadHelper = threadHelper ?? throw new ArgumentNullException(nameof(threadHelper));
            _stateController = stateController ?? throw new ArgumentNullException(nameof(stateController));
            _contextBuilder = contextBuilder;
            _applicationPaths = applicationPaths ?? throw new ArgumentNullException(nameof(applicationPaths));
            _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));

            Port = _settings.MCPServerPort;
            _toolExportProfileSetting = _settings.MCPToolExportProfile;
            InteractionLog = new MCPInteractionLog();
            _settings.OnSettingsChanged += HandleSettingsChanged;
            DomainReloadHandler.Register(_stateController);
        }

        public async Task<bool> StartAsync(CancellationToken ct = default)
        {
            if (_disposed)
            {
                Debug.LogWarning("[GameBooom MCP Server] Cannot start: service is disposed");
                return false;
            }

            if (_isRunning)
            {
                Debug.Log("[GameBooom MCP Server] Server is already running");
                return true;
            }

            try
            {
                Port = _settings.MCPServerPort;
                _toolExportProfileSetting = _settings.MCPToolExportProfile;
                Debug.Log("[GameBooom MCP Server] Starting server...");

                _transport = new HttpMCPTransport(Port);
                var toolExporter = new MCPToolExporter(_settings);
                var executionBridge = new MCPExecutionBridge(_threadHelper, _settings, _stateController, _invoker, InteractionLog);
                _resourceProvider = new MCPResourceProvider(_contextBuilder, _applicationPaths, InteractionLog);
                var promptProvider = new MCPPromptProvider(Application.productName, _applicationPaths.ProjectPath);
                _requestHandler = new MCPRequestHandler(
                    toolExporter,
                    executionBridge,
                    _resourceProvider,
                    promptProvider,
                    "GameBooom MCP Server - " + Application.productName,
                    PackageVersionUtility.CurrentVersion);

                _transport.OnRequestReceived += HandleRequestReceived;

                var started = await _transport.StartAsync(ct);
                if (started)
                {
                    _isRunning = true;
                    Debug.Log($"[GameBooom] MCP Server started on http://127.0.0.1:{Port}/ 🚀 If this tool saves you time, please consider giving it a ⭐ on GitHub: https://github.com/FunseaAI/unity-mcp");
                    CheckForInterruptedExecution();
                    return true;
                }

                Debug.LogError("[GameBooom MCP Server] Failed to start transport");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameBooom MCP Server] Failed to start: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public async Task StopAsync()
        {
            if (!_isRunning) return;

            try
            {
                Debug.Log("[GameBooom MCP Server] Stopping server...");

                if (_transport != null)
                {
                    _transport.OnRequestReceived -= HandleRequestReceived;
                    await _transport.StopAsync();
                    _transport.Dispose();
                    _transport = null;
                }

                _requestHandler = null;
                _resourceProvider?.Dispose();
                _resourceProvider = null;
                _isRunning = false;
                Debug.Log("[GameBooom] MCP Server stopped");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameBooom MCP Server] Error stopping server: {ex.Message}");
            }
        }

        private async void HandleRequestReceived(MCPRequest request, Action<MCPResponse> sendResponse)
        {
            try
            {
                var response = await _threadHelper.ExecuteAsyncOnEditorThreadAsync(
                    async () => await _requestHandler.HandleRequestAsync(request, default));
                sendResponse(response);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameBooom MCP Server] Error handling request: {ex.Message}");
                sendResponse(new MCPResponse
                {
                    Id = request?.Id,
                    Error = new MCPError { Code = -32603, Message = $"Internal error: {ex.Message}" }
                });
            }
        }

        private void HandleSettingsChanged()
        {
            if (_disposed) return;

            var portChanged = _settings.MCPServerPort != Port;
            var profileChanged = !string.Equals(_settings.MCPToolExportProfile, _toolExportProfileSetting, StringComparison.Ordinal);

            if ((portChanged || profileChanged) && _isRunning)
            {
                Debug.Log("[GameBooom MCP Server] Server settings changed, restarting MCP transport...");
                Port = _settings.MCPServerPort;
                _toolExportProfileSetting = _settings.MCPToolExportProfile;

                _ = Task.Run(async () =>
                {
                    await StopAsync();
                    await Task.Delay(500);
                    await StartAsync();
                });
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _settings.OnSettingsChanged -= HandleSettingsChanged;
            _ = StopAsync();
        }

        private void CheckForInterruptedExecution()
        {
            if (_recoveryChecked)
                return;

            _recoveryChecked = true;

            var interrupted = DomainReloadHandler.ConsumeInterruptedState();
            if (interrupted == null)
                return;

            if (!DomainReloadHandler.CanAutoResume())
            {
                var summary = interrupted.GetDescription() +
                              " Auto-recovery paused after too many consecutive recompilations. Retry the tool manually.";
                PublishRecoverySummary(interrupted, summary, MCPToolCallStatus.Error);
                DomainReloadHandler.ResetResumeCounter();
                return;
            }

            DomainReloadHandler.RecordAutoResume();
            WaitForCompilationThen(() =>
            {
                _stateController.ClearState();

                var scriptResult = TempScriptRunner.ConsumeResult();
                var summary = interrupted.GetDescription();
                if (!string.IsNullOrEmpty(scriptResult))
                {
                    summary += "\nContinuation result:\n" + scriptResult;
                }
                else
                {
                    summary += " The MCP server recovered after reload. Re-run the tool if more work is needed.";
                }

                var status = IsErrorResult(scriptResult) || string.IsNullOrEmpty(scriptResult)
                    ? MCPToolCallStatus.Error
                    : MCPToolCallStatus.Success;

                PublishRecoverySummary(interrupted, summary, status);
            });
        }

        private void PublishRecoverySummary(
            DomainReloadHandler.InterruptedState interrupted,
            string summary,
            MCPToolCallStatus status)
        {
            var toolName = interrupted.PendingFunction?.FunctionName;
            if (string.IsNullOrEmpty(toolName))
                toolName = "domain_reload";

            DomainReloadHandler.StoreRecoveryInfo(toolName, status.ToString(), summary);
            InteractionLog.Add(toolName, status, summary);

            if (status == MCPToolCallStatus.Success)
                Debug.Log($"[GameBooom MCP Server] Recovery completed for '{toolName}'. {summary}");
            else
                Debug.LogWarning($"[GameBooom MCP Server] Recovery detected for '{toolName}'. {summary}");
        }

        private static bool IsErrorResult(string scriptResult)
        {
            if (string.IsNullOrEmpty(scriptResult))
                return false;

            return scriptResult.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ||
                   scriptResult.StartsWith("Compilation failed", StringComparison.OrdinalIgnoreCase);
        }

        private static void WaitForCompilationThen(Action onReady)
        {
            if (!EditorApplication.isCompiling)
            {
                EditorApplication.delayCall += () => onReady();
                return;
            }

            void CheckCompilation()
            {
                if (EditorApplication.isCompiling)
                    return;

                EditorApplication.update -= CheckCompilation;
                EditorApplication.delayCall += () => onReady();
            }

            EditorApplication.update += CheckCompilation;
        }
    }
}
