// Copyright (C) GameBooom. Licensed under MIT.

using GameBooom.Editor.MCP.Server;
using GameBooom.Editor.Services;
using GameBooom.Editor.Services.UnityLogs;
using GameBooom.Editor.Settings;
using GameBooom.Editor.State;
using GameBooom.Editor.Threading;
using GameBooom.Editor.Tools;

namespace GameBooom.Editor.DI
{
    internal static class ServiceRegistration
    {
        public static ServiceCollection RegisterServices(this ServiceCollection services)
        {
            // Core Infrastructure (Singletons)
            services.AddSingleton<IApplicationPaths, ApplicationPaths>();
            services.AddSingleton<IEditorStateService, EditorStateService>();
            services.AddSingleton<IEditorContextBuilder, EditorContextBuilder>();
            services.AddSingleton<ISettingsController, SettingsController>();
            services.AddSingleton<IEditorThreadHelper, EditorThreadHelper>();

            // Services (Singletons)
            services.AddSingleton<ICompilationService, CompilationService>();
            services.AddSingleton<UnityLogsRepository, UnityLogsRepository>();
            services.AddSingleton<FunctionInvokerController, FunctionInvokerController>();

            // MCP Server (Singleton)
            services.AddSingleton<MCPServerService, MCPServerService>();

            // State (Scoped)
            services.AddScoped<IStateController, StateController>();

            return services;
        }
    }
}
