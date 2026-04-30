// Copyright (C) Funplay. Licensed under MIT.

using System;
using Funplay.Editor.Settings;
using UnityEditor;
using Funplay.Editor.DI;
using UnityEngine;

namespace Funplay.Editor.MCP.Server
{
    /// <summary>
    /// Handles Unity domain reload for the MCP server.
    /// Saves server state before reload and restarts after reload if it was running.
    /// </summary>
    [InitializeOnLoad]
    internal static class MCPServerDomainReloadHandler
    {
        private const string WasRunningKey = "Funplay_MCPServer_WasRunning";
        private const string PortKey = "Funplay_MCPServer_Port";

        static MCPServerDomainReloadHandler()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterReload;
        }

        internal static void PrepareForReload(IServiceProvider services)
        {
            try
            {
                var mcpServer = services?.GetService(typeof(MCPServerService)) as MCPServerService;
                if (mcpServer?.IsRunning != true)
                    return;

                PluginDebugLogger.Log("[Funplay MCP Server] Saving state before domain reload");
                SessionState.SetBool(WasRunningKey, true);
                SessionState.SetInt(PortKey, mcpServer.Port);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Funplay MCP Server] Error preparing reload state: {ex.Message}");
            }
        }

        private static void OnBeforeReload()
        {
            try
            {
                PrepareForReload(RootScopeServices.Services);

                var mcpServer = RootScopeServices.Services?.GetService(typeof(MCPServerService)) as MCPServerService;
                if (mcpServer?.IsRunning == true)
                {
                    _ = mcpServer.StopAsync();
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Funplay MCP Server] Error in OnBeforeReload: {ex.Message}");
            }
        }

        private static void OnAfterReload()
        {
            try
            {
                if (SessionState.GetBool(WasRunningKey, false))
                {
                    PluginDebugLogger.Log("[Funplay MCP Server] Restarting server after domain reload");

                    EditorApplication.delayCall += () =>
                    {
                        try
                        {
                            int savedPort = SessionState.GetInt(PortKey, -1);
                            var settings = RootScopeServices.Services?.GetService(typeof(ISettingsController)) as ISettingsController;
                            if (savedPort > 0 && settings != null && settings.MCPServerPort != savedPort)
                            {
                                settings.MCPServerPort = savedPort;
                            }

                            var mcpServer = RootScopeServices.Services?.GetService(typeof(MCPServerService)) as MCPServerService;
                            if (mcpServer != null)
                            {
                                _ = mcpServer.StartAsync();
                            }
                            else
                            {
                                Debug.LogWarning("[Funplay MCP Server] Could not restart: service not found");
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError($"[Funplay MCP Server] Error restarting after reload: {ex.Message}");
                        }
                        finally
                        {
                            SessionState.EraseBool(WasRunningKey);
                            SessionState.EraseInt(PortKey);
                        }
                    };
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Funplay MCP Server] Error in OnAfterReload: {ex.Message}");
            }
        }
    }
}
