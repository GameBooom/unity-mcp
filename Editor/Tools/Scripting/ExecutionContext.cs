// Copyright (C) Funplay. Licensed under MIT.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace Funplay.Editor.Tools.Scripting
{
    /// <summary>
    /// Injected into <see cref="IFunplayCommand.Execute"/> by <c>execute_code</c>.
    ///
    /// Use the Register/Destroy methods instead of touching <c>Undo</c> directly — that way
    /// every snippet automatically participates in editor Undo and the host returns a
    /// structured changelog (created/modified/destroyed instance ids) to the agent.
    ///
    /// Logs gathered through <see cref="Log"/>/<see cref="LogWarning"/>/<see cref="LogError"/>
    /// are returned verbatim in the tool response. They do NOT also go to the Unity console
    /// unless the snippet explicitly calls <c>Debug.Log</c>.
    /// </summary>
    public sealed class ExecutionContext
    {
        public sealed class LogEntry
        {
            public string Level; // "info" / "warning" / "error"
            public string Message;
        }

        private readonly List<LogEntry> _logs = new List<LogEntry>();
        private readonly List<long> _created = new List<long>();
        private readonly List<long> _modified = new List<long>();
        private readonly List<long> _destroyed = new List<long>();

        public IReadOnlyList<LogEntry> Logs => _logs;
        public IReadOnlyList<long> CreatedInstanceIds => _created;
        public IReadOnlyList<long> ModifiedInstanceIds => _modified;
        public IReadOnlyList<long> DestroyedInstanceIds => _destroyed;

        /// <summary>Object that the snippet returns explicitly (optional). Serialized into the response.</summary>
        public object ReturnValue { get; set; }

        // ----- Undo + tracking -----

        public void RegisterObjectCreation(UnityObject obj)
        {
            if (obj == null) return;
            Undo.RegisterCreatedObjectUndo(obj, "execute_code: create");
            _created.Add(obj.GetInstanceID());
        }

        public void RegisterObjectModification(UnityObject obj)
        {
            if (obj == null) return;
            Undo.RecordObject(obj, "execute_code: modify");
            _modified.Add(obj.GetInstanceID());
        }

        public void DestroyObject(UnityObject obj)
        {
            if (obj == null) return;
            _destroyed.Add(obj.GetInstanceID());
            Undo.DestroyObjectImmediate(obj);
        }

        // ----- Logging -----

        public void Log(string format, params object[] args)
            => _logs.Add(new LogEntry { Level = "info", Message = Format(format, args) });

        public void LogWarning(string format, params object[] args)
            => _logs.Add(new LogEntry { Level = "warning", Message = Format(format, args) });

        public void LogError(string format, params object[] args)
            => _logs.Add(new LogEntry { Level = "error", Message = Format(format, args) });

        private static string Format(string format, object[] args)
        {
            if (args == null || args.Length == 0) return format ?? string.Empty;
            try { return string.Format(format ?? string.Empty, args); }
            catch { return (format ?? string.Empty) + " " + string.Join(", ", args); }
        }
    }
}
