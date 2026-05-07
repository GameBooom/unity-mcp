// Copyright (C) Funplay. Licensed under MIT.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Funplay.Editor.Tools.Helpers
{
    /// <summary>
    /// O(1) type lookup for UnityEngine.Object-derived types (components, assets, scriptable objects).
    /// Built once from <see cref="TypeCache"/> on first access — replaces per-call AppDomain scans.
    /// Names lookup is case-insensitive.
    /// </summary>
    public static class TypeResolver
    {
        private static Dictionary<string, Type> s_byName;
        private static Dictionary<string, Type> s_byFullName;
        private static readonly object s_lock = new object();

        private static void EnsureBuilt()
        {
            if (s_byName != null)
                return;

            lock (s_lock)
            {
                if (s_byName != null)
                    return;

                var byName = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
                var byFullName = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

                foreach (var type in TypeCache.GetTypesDerivedFrom<UnityEngine.Object>())
                {
                    if (type == null)
                        continue;

                    if (!string.IsNullOrEmpty(type.Name) && !byName.ContainsKey(type.Name))
                        byName.Add(type.Name, type);

                    if (!string.IsNullOrEmpty(type.FullName) && !byFullName.ContainsKey(type.FullName))
                        byFullName.Add(type.FullName, type);
                }

                s_byName = byName;
                s_byFullName = byFullName;
            }
        }

        /// <summary>
        /// Resolve a type name (short or fully qualified) to a UnityEngine.Object-derived <see cref="Type"/>.
        /// Returns null if not found.
        /// </summary>
        public static Type Resolve(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            EnsureBuilt();

            if (s_byFullName.TryGetValue(typeName, out var t))
                return t;
            if (s_byName.TryGetValue(typeName, out t))
                return t;

            // UnityEngine.X shorthand
            if (!typeName.Contains("."))
            {
                if (s_byFullName.TryGetValue("UnityEngine." + typeName, out t))
                    return t;
                if (s_byFullName.TryGetValue("UnityEngine.UI." + typeName, out t))
                    return t;
                if (s_byFullName.TryGetValue("UnityEngine.EventSystems." + typeName, out t))
                    return t;
            }

            return null;
        }

        /// <summary>
        /// Resolve a Component type by name. Returns null if the name resolves to a non-Component type.
        /// </summary>
        public static Type ResolveComponent(string typeName)
        {
            var type = Resolve(typeName);
            return type != null && typeof(Component).IsAssignableFrom(type) ? type : null;
        }

        /// <summary>
        /// Force the cache to rebuild on next access. Call after assembly reload changes types.
        /// </summary>
        public static void Invalidate()
        {
            lock (s_lock)
            {
                s_byName = null;
                s_byFullName = null;
            }
        }
    }
}
