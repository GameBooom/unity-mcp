// Copyright (C) GameBooom. Licensed under MIT.

using System.IO;
using UnityEngine;

namespace GameBooom.Editor.Services
{
    internal class ApplicationPaths : IApplicationPaths
    {
        public string ProjectPath => Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
        public string AssetsPath => Application.dataPath;
        public string TempPath => Path.Combine(ProjectPath, "Temp", "GameBooom");
        public string DataPath => AssetsPath;
        public string PersistentDataPath => Application.persistentDataPath;
    }
}
