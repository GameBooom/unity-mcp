// Copyright (C) GameBooom. Licensed under MIT.

using System;
using GameBooom.Editor.MCP.Server;
using GameBooom.Editor.Settings;
using GameBooom.Editor.Services.UnityLogs;
using UnityEditor;
using UnityEngine;

namespace GameBooom.Editor.DI
{
    [InitializeOnLoad]
    internal static class RootScopeServices
    {
        private static ServiceProvider _serviceProvider;

        public static IServiceProvider Services => _serviceProvider;

        static RootScopeServices()
        {
            Initialize();
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private static void Initialize()
        {
            try
            {
                var services = new ServiceCollection();
                services.RegisterServices();
                _serviceProvider = services.BuildServiceProvider();
                Debug.Log("[GameBooom] Root services initialized.");

                var unityLogsRepository =
                    _serviceProvider.GetService(typeof(UnityLogsRepository)) as UnityLogsRepository;
                unityLogsRepository?.StartListening();

                var settings = _serviceProvider.GetService(typeof(ISettingsController)) as ISettingsController;
                if (settings?.MCPServerEnabled == true)
                {
                    var mcpServer = _serviceProvider.GetService(typeof(MCPServerService)) as MCPServerService;
                    if (mcpServer != null)
                    {
                        _ = mcpServer.StartAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameBooom] Failed to initialize root services: {ex}");
            }
        }

        private static void OnBeforeAssemblyReload()
        {
            try
            {
                MCPServerDomainReloadHandler.PrepareForReload(_serviceProvider);
                _serviceProvider?.Dispose();
                _serviceProvider = null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameBooom] Error disposing root services: {ex}");
            }
        }
    }
}
