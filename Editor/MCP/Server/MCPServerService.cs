// Copyright (C) Funplay. Licensed under MIT.

using System;
using System.Threading;
using System.Threading.Tasks;
using Funplay.Editor.MCP;
using Funplay.Editor.Services;
using Funplay.Editor.Settings;
using Funplay.Editor.State;
using Funplay.Editor.Threading;
using Funplay.Editor.Tools;
using Funplay.Editor.Tools.Builtins;
using UnityEditor;
using UnityEngine;

namespace Funplay.Editor.MCP.Server
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
        private readonly ICompilationService _compilationService;
        private readonly FunctionInvokerController _invoker;

        private IMCPTransport _transport;
        private MCPRequestHandler _requestHandler;
        private MCPResourceProvider _resourceProvider;
        private bool _isRunning;
        private bool _disposed;
        private bool _recoveryChecked;
        private bool _restartScheduled;
        private bool _restartInProgress;
        private string _toolExposureSetting;

        public bool IsRunning => _isRunning;
        public int Port { get; private set; }
        public MCPInteractionLog InteractionLog { get; }

        public MCPServerService(
            ISettingsController settings,
            IEditorThreadHelper threadHelper,
            IStateController stateController,
            IEditorContextBuilder contextBuilder,
            IApplicationPaths applicationPaths,
            ICompilationService compilationService,
            FunctionInvokerController invoker)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _threadHelper = threadHelper ?? throw new ArgumentNullException(nameof(threadHelper));
            _stateController = stateController ?? throw new ArgumentNullException(nameof(stateController));
            _contextBuilder = contextBuilder;
            _applicationPaths = applicationPaths ?? throw new ArgumentNullException(nameof(applicationPaths));
            _compilationService = compilationService ?? throw new ArgumentNullException(nameof(compilationService));
            _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));

            Port = _settings.MCPServerPort;
            _toolExposureSetting = BuildToolExposureSetting();
            InteractionLog = new MCPInteractionLog();
            _settings.OnSettingsChanged += HandleSettingsChanged;
            DomainReloadHandler.Register(_stateController);
        }

        public async Task<bool> StartAsync(CancellationToken ct = default)
        {
            if (_disposed)
            {
                Debug.LogWarning("[Funplay MCP Server] Cannot start: service is disposed");
                return false;
            }

            if (_isRunning)
            {
                PluginDebugLogger.Log("[Funplay MCP Server] Server is already running");
                return true;
            }

            try
            {
                Port = ResolveStartupPort();
                _toolExposureSetting = BuildToolExposureSetting();
                PluginDebugLogger.Log("[Funplay MCP Server] Starting server...");

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
                    "Funplay MCP Server - " + Application.productName,
                    PackageVersionUtility.CurrentVersion);

                _transport.OnRequestReceived += HandleRequestReceived;

                var started = await _transport.StartAsync(ct);
                if (started)
                {
                    _isRunning = true;
                    PluginDebugLogger.Log($"[Funplay] MCP Server started on http://127.0.0.1:{Port}/ If this tool saves you time, please consider giving it a Star on GitHub: https://github.com/FunplayAI/funplay-unity-mcp");
                    ExternalSyncRecoveryTracker.TryCompletePendingRecovery();
                    CheckForInterruptedExecution();
                    return true;
                }

                Debug.LogError("[Funplay MCP Server] Failed to start transport");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Funplay MCP Server] Failed to start: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public async Task StopAsync()
        {
            if (!_isRunning) return;

            try
            {
                PluginDebugLogger.Log("[Funplay MCP Server] Stopping server...");

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
                PluginDebugLogger.Log("[Funplay] MCP Server stopped");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Funplay MCP Server] Error stopping server: {ex.Message}");
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
                Debug.LogError($"[Funplay MCP Server] Error handling request: {ex.Message}");
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
            var toolExposureSetting = BuildToolExposureSetting();
            var toolExposureChanged = !string.Equals(toolExposureSetting, _toolExposureSetting, StringComparison.Ordinal);

            if ((portChanged || toolExposureChanged) && _isRunning)
            {
                PluginDebugLogger.Log("[Funplay MCP Server] Server settings changed, restarting MCP transport...");
                Port = _settings.MCPServerPort;
                _toolExposureSetting = toolExposureSetting;
                ScheduleRestart();
            }
        }

        private string BuildToolExposureSetting()
        {
            return string.Join("|",
                _settings.MCPToolExportProfile ?? string.Empty,
                _settings.MCPCoreToolsConfigured ? "core=custom" : "core=default",
                string.Join(",", _settings.MCPCoreTools ?? Array.Empty<string>()),
                _settings.MCPFullToolsConfigured ? "full=custom" : "full=default",
                string.Join(",", _settings.MCPFullTools ?? Array.Empty<string>()));
        }

        private int ResolveStartupPort()
        {
            var configuredPort = NormalizePort(_settings.MCPServerPort);
            return configuredPort;
        }

        private static int NormalizePort(int port)
        {
            return port > 0 ? port : 8765;
        }

        private void ScheduleRestart()
        {
            if (_disposed || _restartScheduled)
                return;

            _restartScheduled = true;
            EditorApplication.delayCall += RestartTransportAfterSettingsChange;
        }

        private async void RestartTransportAfterSettingsChange()
        {
            _restartScheduled = false;

            if (_disposed)
                return;

            if (_restartInProgress)
            {
                ScheduleRestart();
                return;
            }

            _restartInProgress = true;
            try
            {
                await StopAsync();

                if (_disposed)
                    return;

                EditorApplication.delayCall += StartTransportAfterSettingsChange;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Funplay MCP Server] Failed while restarting after settings change: {ex.Message}");
                _restartInProgress = false;
            }
        }

        private async void StartTransportAfterSettingsChange()
        {
            try
            {
                if (!_disposed)
                    await StartAsync();
            }
            finally
            {
                _restartInProgress = false;
                if (_restartScheduled && !_disposed)
                    EditorApplication.delayCall += RestartTransportAfterSettingsChange;
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
                if (IsSyncExternalChanges(interrupted))
                {
                    var compilationSummary = BuildSyncExternalChangesRecoverySummary();
                    summary += "\n" + compilationSummary.Summary;
                    PublishRecoverySummary(interrupted, summary, compilationSummary.Status);
                    return;
                }

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

        private bool IsSyncExternalChanges(DomainReloadHandler.InterruptedState interrupted)
        {
            return string.Equals(
                interrupted?.PendingFunction?.FunctionName,
                "request_recompile",
                StringComparison.OrdinalIgnoreCase);
        }

        private (string Summary, MCPToolCallStatus Status) BuildSyncExternalChangesRecoverySummary()
        {
            var issues = _compilationService.GetCompilationErrors(includeWarnings: true);
            var hasIssues = !string.Equals(issues, "No compilation errors or warnings detected.", StringComparison.Ordinal) &&
                            !string.Equals(issues, "No compilation errors detected.", StringComparison.Ordinal);

            if (hasIssues)
            {
                return ("External changes were imported, but compilation reported issues.\n" + issues, MCPToolCallStatus.Error);
            }

            return ("External changes were imported and script compilation finished successfully after domain reload.", MCPToolCallStatus.Success);
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
                PluginDebugLogger.Log($"[Funplay MCP Server] Recovery completed for '{toolName}'. {summary}");
            else
                Debug.LogWarning($"[Funplay MCP Server] Recovery detected for '{toolName}'. {summary}");
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
