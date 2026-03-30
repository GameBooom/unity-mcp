// Copyright (C) GameBooom. Licensed under MIT.

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GameBooom.Editor.Services.UnityLogs
{
    internal class UnityLogsRepository : IDisposable
    {
        private const int MaxLogs = 200;

        private readonly List<LogEntry> _logs = new List<LogEntry>();
        private readonly object _lock = new object();
        private bool _isListening;

        public void StartListening()
        {
            if (_isListening)
                return;

            _isListening = true;
            Application.logMessageReceived += OnLogReceived;
        }

        public void StopListening()
        {
            if (!_isListening)
                return;

            _isListening = false;
            Application.logMessageReceived -= OnLogReceived;
        }

        public string GetRecentLogs(string logType = "all", int count = 30, int sinceSeconds = 0)
        {
            count = Mathf.Clamp(count, 1, 200);
            var filter = (logType ?? "all").ToLowerInvariant();
            var cutoff = sinceSeconds > 0 ? DateTime.Now.AddSeconds(-sinceSeconds) : (DateTime?)null;

            List<LogEntry> snapshot;
            lock (_lock)
            {
                snapshot = new List<LogEntry>(_logs);
            }

            if (snapshot.Count == 0)
                return null;

            var sb = new StringBuilder();
            int matchCount = 0;

            for (int i = snapshot.Count - 1; i >= 0 && matchCount < count; i--)
            {
                var entry = snapshot[i];
                if (cutoff.HasValue && entry.Timestamp < cutoff.Value)
                    continue;

                if (!MatchesFilter(entry.Type, filter))
                    continue;

                sb.AppendLine($"[{ToLabel(entry.Type)}] {FirstLine(entry.Message)}");
                matchCount++;
            }

            if (matchCount == 0)
            {
                if (sinceSeconds > 0)
                    return $"No {filter} entries found in cached logs from the last {sinceSeconds} second(s)";

                return $"No {filter} entries found in cached logs";
            }

            var timeSuffix = sinceSeconds > 0 ? $", last {sinceSeconds}s" : string.Empty;
            return $"Console logs ({matchCount} entries, filter: {filter}, source: cache{timeSuffix}):\n{sb}";
        }

        public void Clear()
        {
            lock (_lock)
            {
                _logs.Clear();
            }
        }

        private void OnLogReceived(string message, string stackTrace, LogType type)
        {
            if (string.IsNullOrEmpty(message))
                return;

            if (message.StartsWith("[GameBooom]", StringComparison.Ordinal) ||
                message.StartsWith("[GameBooom MCP Server]", StringComparison.Ordinal))
            {
                return;
            }

            lock (_lock)
            {
                _logs.Add(new LogEntry
                {
                    Message = message,
                    StackTrace = stackTrace,
                    Type = type,
                    Timestamp = DateTime.Now
                });

                while (_logs.Count > MaxLogs)
                    _logs.RemoveAt(0);
            }
        }

        private static bool MatchesFilter(LogType type, string filter)
        {
            switch (filter)
            {
                case "error":
                    return type == LogType.Error || type == LogType.Assert || type == LogType.Exception;
                case "warning":
                    return type == LogType.Warning;
                case "log":
                    return type == LogType.Log;
                default:
                    return true;
            }
        }

        private static string ToLabel(LogType type)
        {
            switch (type)
            {
                case LogType.Warning:
                    return "WARN";
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    return "ERROR";
                default:
                    return "LOG";
            }
        }

        private static string FirstLine(string message)
        {
            return string.IsNullOrEmpty(message) ? string.Empty : message.Split('\n')[0];
        }

        public void Dispose()
        {
            StopListening();
        }

        private class LogEntry
        {
            public string Message { get; set; }
            public string StackTrace { get; set; }
            public LogType Type { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }
}
