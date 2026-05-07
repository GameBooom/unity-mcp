// Copyright (C) Funplay. Licensed under MIT.
using System;
using System.Linq;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using Funplay.Editor.Tools.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Funplay.Editor.Tools.Builtins
{
    /// <summary>
    /// Read and write component fields through SerializedObject — picks up
    /// <c>[SerializeField] private</c>, supports Object references via
    /// <c>{"fileID": instanceId}</c>, returns per-field success so agents can
    /// recover from partial writes.
    /// </summary>
    [ToolProvider("ComponentProperty")]
    internal static class ComponentPropertyFunctions
    {
        [Description("List components on a GameObject. Each entry includes its instanceId so subsequent " +
                     "set_component_property calls can disambiguate when a GameObject has multiple components of the same type.")]
        [ReadOnlyTool]
        public static object ListComponents(
            [ToolParam("GameObject identifier (instance id, name, path, tag…)")] string target,
            [ToolParam("How to resolve target", Required = false)] string find_method = null)
        {
            var go = ObjectsHelper.FindObject(target, find_method, searchInactive: true);
            if (go == null)
                return Response.Error("TARGET_NOT_FOUND", new { target, find_method });

            var items = go.GetComponents<Component>()
                .Where(c => c != null)
                .Select(c => new { instanceId = (long)c.GetInstanceID(), type = c.GetType().Name, fullType = c.GetType().FullName })
                .ToList();

            return Response.Success($"{items.Count} component(s) on '{go.name}'.", new
            {
                gameObject = new { instanceId = (long)go.GetInstanceID(), name = go.name },
                components = items
            });
        }

        [Description("Get all serialized properties on a component, including [SerializeField] private fields. " +
                     "Component can be addressed by type name on a GameObject, or directly by component instanceId.")]
        [ReadOnlyTool]
        public static object GetComponentProperties(
            [ToolParam("GameObject identifier (omit if using component_instance_id)", Required = false)] string target = null,
            [ToolParam("Component type name (e.g. 'Rigidbody'). Omit if using component_instance_id.", Required = false)] string component = null,
            [ToolParam("Component instanceId (alternative to target+component)", Required = false)] string component_instance_id = null,
            [ToolParam("How to resolve target", Required = false)] string find_method = null)
        {
            var resolved = ResolveComponent(target, component, component_instance_id, find_method);
            if (resolved.Error != null) return resolved.Error;

            var props = ComponentSerializer.ReadProperties(resolved.Component);
            return Response.Success(
                $"{props.Count} properties on {resolved.Component.GetType().Name}.",
                new
                {
                    componentInstanceId = (long)resolved.Component.GetInstanceID(),
                    type = resolved.Component.GetType().Name,
                    gameObject = new { instanceId = (long)resolved.Component.gameObject.GetInstanceID(), name = resolved.Component.gameObject.name },
                    properties = props
                });
        }

        [Description("Set a single property or field on a component. " +
                     "Use simple JSON for value (e.g. '5', 'true', '\"text\"', '[1,2,3]'). " +
                     "For Object references pass {\"fileID\": <instanceId>} or {\"assetPath\": \"Assets/...\"}.")]
        [SceneEditingTool]
        public static object SetComponentProperty(
            [ToolParam("GameObject identifier (omit if using component_instance_id)", Required = false)] string target = null,
            [ToolParam("Component type name (omit if using component_instance_id)", Required = false)] string component = null,
            [ToolParam("Property/field name to set")] string property = null,
            [ToolParam("New value as JSON literal")] string value = null,
            [ToolParam("Component instanceId (alternative to target+component)", Required = false)] string component_instance_id = null,
            [ToolParam("How to resolve target", Required = false)] string find_method = null)
        {
            if (string.IsNullOrEmpty(property))
                return Response.Error("PROPERTY_REQUIRED");
            if (value == null)
                return Response.Error("VALUE_REQUIRED");

            var resolved = ResolveComponent(target, component, component_instance_id, find_method);
            if (resolved.Error != null) return resolved.Error;

            JToken token;
            try { token = ParseJsonValue(value); }
            catch (Exception ex) { return Response.Error($"INVALID_VALUE_JSON: {ex.Message}"); }

            var props = new JObject { [property] = token };
            var results = ComponentSerializer.WriteProperties(resolved.Component, props,
                $"Set {property} on {resolved.Component.GetType().Name}");

            var first = results.Count > 0 ? results[0] : null;
            if (first == null || !first.Success)
            {
                return Response.Error("PROPERTY_SET_FAILED",
                    new { property, error = first?.Error ?? "unknown" });
            }

            return Response.Success($"Set {resolved.Component.GetType().Name}.{property}.",
                new { componentInstanceId = (long)resolved.Component.GetInstanceID(), property });
        }

        [Description("Set multiple properties on a component in one call. " +
                     "Pass `properties` as a JSON object: {\"mass\": 5, \"isKinematic\": true, \"material\": {\"fileID\": 12345}}. " +
                     "Returns per-field success so partial failures are diagnosable.")]
        [SceneEditingTool]
        public static object SetComponentProperties(
            [ToolParam("GameObject identifier (omit if using component_instance_id)", Required = false)] string target = null,
            [ToolParam("Component type name (omit if using component_instance_id)", Required = false)] string component = null,
            [ToolParam("JSON object of property→value pairs")] string properties = null,
            [ToolParam("Component instanceId (alternative to target+component)", Required = false)] string component_instance_id = null,
            [ToolParam("How to resolve target", Required = false)] string find_method = null)
        {
            if (string.IsNullOrWhiteSpace(properties))
                return Response.Error("PROPERTIES_REQUIRED");

            var resolved = ResolveComponent(target, component, component_instance_id, find_method);
            if (resolved.Error != null) return resolved.Error;

            JObject jobj;
            try { jobj = JObject.Parse(properties); }
            catch (Exception ex) { return Response.Error($"INVALID_PROPERTIES_JSON: {ex.Message}"); }

            var results = ComponentSerializer.WriteProperties(resolved.Component, jobj,
                $"Set properties on {resolved.Component.GetType().Name}");

            int success = results.Count(r => r.Success);
            int fail = results.Count - success;
            return Response.Success(
                $"Applied {success} of {results.Count} field(s) on {resolved.Component.GetType().Name}.",
                new
                {
                    componentInstanceId = (long)resolved.Component.GetInstanceID(),
                    successCount = success,
                    failCount = fail,
                    fields = results
                });
        }

        // -------- Helpers --------

        private struct ResolvedComponent
        {
            public Component Component;
            public object Error;
        }

        private static ResolvedComponent ResolveComponent(string target, string componentName, string componentInstanceId, string findMethod)
        {
            // Direct component instanceId path (preferred when GameObject has multiple of same type)
            if (!string.IsNullOrEmpty(componentInstanceId) && long.TryParse(componentInstanceId, out var cid))
            {
                var c = ObjectsHelper.FindComponentById(cid);
                if (c == null)
                    return new ResolvedComponent { Error = Response.Error("COMPONENT_NOT_FOUND",
                        new { component_instance_id = componentInstanceId }) };
                return new ResolvedComponent { Component = c };
            }

            if (string.IsNullOrEmpty(target))
                return new ResolvedComponent { Error = Response.Error("TARGET_REQUIRED",
                    new { hint = "Pass either target+component or component_instance_id." }) };

            var go = ObjectsHelper.FindObject(target, findMethod, searchInactive: true);
            if (go == null)
                return new ResolvedComponent { Error = Response.Error("TARGET_NOT_FOUND",
                    new { target, find_method = findMethod }) };

            if (string.IsNullOrEmpty(componentName))
                return new ResolvedComponent { Error = Response.Error("COMPONENT_REQUIRED") };

            // Try TypeResolver-driven exact lookup first (handles full names, namespaced types)
            var type = TypeResolver.ResolveComponent(componentName);
            if (type != null)
            {
                var c = go.GetComponent(type);
                if (c != null) return new ResolvedComponent { Component = c };
            }

            // Fallback: case-insensitive name match across attached components
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                if (string.Equals(c.GetType().Name, componentName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.GetType().FullName, componentName, StringComparison.OrdinalIgnoreCase))
                    return new ResolvedComponent { Component = c };
            }

            var available = string.Join(", ", go.GetComponents<Component>()
                .Where(c => c != null).Select(c => c.GetType().Name));
            return new ResolvedComponent
            {
                Error = Response.Error("COMPONENT_NOT_FOUND_ON_TARGET",
                    new { target = go.name, component = componentName, available })
            };
        }

        // Accept loose values: bare numbers/booleans, quoted strings, JSON objects/arrays.
        private static JToken ParseJsonValue(string raw)
        {
            raw = raw.Trim();
            if (raw.Length == 0) return JValue.CreateString(string.Empty);
            if (raw.StartsWith("{") || raw.StartsWith("[") || raw.StartsWith("\""))
                return JToken.Parse(raw);
            if (bool.TryParse(raw, out var b)) return new JValue(b);
            if (long.TryParse(raw, out var l)) return new JValue(l);
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d))
                return new JValue(d);
            return new JValue(raw); // treat as string
        }
    }
}
