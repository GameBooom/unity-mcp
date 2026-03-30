// Copyright (C) GameBooom. Licensed under MIT.

namespace GameBooom.Editor.Services
{
    internal interface IApplicationPaths
    {
        string ProjectPath { get; }
        string AssetsPath { get; }
        string TempPath { get; }
        string DataPath { get; }
        string PersistentDataPath { get; }
    }
}
