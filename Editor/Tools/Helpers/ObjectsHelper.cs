// Copyright (C) Funplay. Licensed under MIT.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Funplay.Editor.Tools.Helpers
{
    /// <summary>
    /// Unified GameObject locator. All Funplay tools should resolve scene objects through here
    /// instead of calling <c>GameObject.Find</c> directly — that way name/path/id/tag/layer/component
    /// lookups, inactive-object handling and prefab-stage awareness stay consistent.
    /// </summary>
    public static class ObjectsHelper
    {
        public const string MethodById = "by_id";
        public const string MethodByName = "by_name";
        public const string MethodByPath = "by_path";
        public const string MethodByTag = "by_tag";
        public const string MethodByLayer = "by_layer";
        public const string MethodByComponent = "by_component";
        public const string MethodByIdOrNameOrPath = "by_id_or_name_or_path";

        public static readonly string[] AllMethods =
        {
            MethodById, MethodByName, MethodByPath, MethodByTag,
            MethodByLayer, MethodByComponent, MethodByIdOrNameOrPath
        };

        /// <summary>
        /// Find a single GameObject. If multiple match and findAll is false, returns the first.
        /// </summary>
        public static GameObject FindObject(string target, string searchMethod = null,
            bool searchInactive = false, bool searchInChildren = false, GameObject root = null)
        {
            var list = FindObjects(target, searchMethod, findAll: false, searchInactive, searchInChildren, root);
            return list.Count > 0 ? list[0] : null;
        }

        /// <summary>
        /// Core finder. <paramref name="findAll"/> false returns at most one element (the first match).
        /// When the active prefab stage is open it is searched in addition to the active scene.
        /// </summary>
        public static List<GameObject> FindObjects(string target, string searchMethod = null,
            bool findAll = true, bool searchInactive = false, bool searchInChildren = false,
            GameObject root = null)
        {
            var results = new List<GameObject>();
            if (string.IsNullOrEmpty(target))
                return results;

            // Auto-detect default method
            if (string.IsNullOrEmpty(searchMethod))
            {
                if (long.TryParse(target, out _))
                    searchMethod = MethodById;
                else if (target.Contains('/'))
                    searchMethod = MethodByPath;
                else
                    searchMethod = MethodByName;
            }

            // Resolve a child-search root first, if requested
            GameObject rootObj = root;
            if (searchInChildren && rootObj == null)
            {
                rootObj = FindObject(target, MethodByIdOrNameOrPath, searchInactive: true);
                if (rootObj == null)
                    return results;
            }

            switch (searchMethod)
            {
                case MethodById:
                {
                    var go = ObjectIdHelper.ToObject(target) as GameObject;
                    if (go != null)
                        results.Add(go);
                    break;
                }
                case MethodByName:
                {
                    foreach (var go in EnumerateSearchPool(rootObj, searchInactive))
                    {
                        if (go.name == target)
                            results.Add(go);
                    }
                    break;
                }
                case MethodByPath:
                {
                    if (rootObj != null)
                    {
                        var t = rootObj.transform.Find(target);
                        if (t != null) results.Add(t.gameObject);
                    }
                    else
                    {
                        // Search every scene including prefab stage
                        foreach (var scene in EnumerateLoadedScenes())
                        {
                            foreach (var sceneRoot in scene.GetRootGameObjects())
                            {
                                if (sceneRoot.name == target.Split('/')[0])
                                {
                                    var rest = target.Contains('/') ? target.Substring(target.IndexOf('/') + 1) : null;
                                    if (rest == null)
                                    {
                                        results.Add(sceneRoot);
                                    }
                                    else
                                    {
                                        var t = sceneRoot.transform.Find(rest);
                                        if (t != null) results.Add(t.gameObject);
                                    }
                                }
                            }
                        }
                        var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                        if (prefabStage != null && prefabStage.prefabContentsRoot != null)
                        {
                            var stageRoot = prefabStage.prefabContentsRoot;
                            if (stageRoot.name == target.Split('/')[0])
                            {
                                var rest = target.Contains('/') ? target.Substring(target.IndexOf('/') + 1) : null;
                                if (rest == null)
                                {
                                    results.Add(stageRoot);
                                }
                                else
                                {
                                    var t = stageRoot.transform.Find(rest);
                                    if (t != null) results.Add(t.gameObject);
                                }
                            }
                        }
                    }
                    break;
                }
                case MethodByTag:
                {
                    foreach (var go in EnumerateSearchPool(rootObj, searchInactive))
                    {
                        try
                        {
                            if (go.CompareTag(target))
                                results.Add(go);
                        }
                        catch (UnityException)
                        {
                            // Tag not defined; skip silently
                        }
                    }
                    break;
                }
                case MethodByLayer:
                {
                    int layerIndex;
                    if (!int.TryParse(target, out layerIndex))
                        layerIndex = LayerMask.NameToLayer(target);
                    if (layerIndex >= 0)
                    {
                        foreach (var go in EnumerateSearchPool(rootObj, searchInactive))
                        {
                            if (go.layer == layerIndex)
                                results.Add(go);
                        }
                    }
                    break;
                }
                case MethodByComponent:
                {
                    var compType = TypeResolver.ResolveComponent(target);
                    if (compType != null)
                    {
                        foreach (var go in EnumerateSearchPool(rootObj, searchInactive))
                        {
                            if (go.GetComponent(compType) != null)
                                results.Add(go);
                        }
                    }
                    break;
                }
                case MethodByIdOrNameOrPath:
                {
                    var byId = ObjectIdHelper.ToObject(target) as GameObject;
                    if (byId != null) { results.Add(byId); break; }
                    if (target.Contains('/'))
                    {
                        // Re-enter as path
                        return FindObjects(target, MethodByPath, findAll, searchInactive, searchInChildren, root);
                    }
                    return FindObjects(target, MethodByName, findAll, searchInactive, searchInChildren, root);
                }
                default:
                    Debug.LogWarning($"[Funplay] Unknown search method '{searchMethod}'");
                    break;
            }

            var distinct = results.Distinct().ToList();
            if (!findAll && distinct.Count > 1)
                return new List<GameObject> { distinct[0] };

            return distinct;
        }

        /// <summary>
        /// Enumerate every GameObject considered "in scope" — active scene roots, additively-loaded
        /// scenes, and the open prefab stage. <paramref name="includeInactive"/> uses
        /// <c>Resources.FindObjectsOfTypeAll</c> to also surface inactive editor-time objects.
        /// </summary>
        private static IEnumerable<GameObject> EnumerateSearchPool(GameObject root, bool includeInactive)
        {
            if (root != null)
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive))
                    yield return t.gameObject;
                yield break;
            }

            if (includeInactive)
            {
                foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (!go.scene.IsValid())
                        continue;
                    if (go.hideFlags == HideFlags.NotEditable || go.hideFlags == HideFlags.HideAndDontSave)
                        continue;
                    yield return go;
                }
                yield break;
            }

            foreach (var scene in EnumerateLoadedScenes())
            {
                foreach (var sceneRoot in scene.GetRootGameObjects())
                {
                    foreach (var t in sceneRoot.GetComponentsInChildren<Transform>(false))
                        yield return t.gameObject;
                }
            }

            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.prefabContentsRoot != null)
            {
                foreach (var t in prefabStage.prefabContentsRoot.GetComponentsInChildren<Transform>(true))
                    yield return t.gameObject;
            }
        }

        private static IEnumerable<Scene> EnumerateLoadedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded)
                    yield return scene;
            }
        }

        /// <summary>
        /// Look up a Component by its instance id. Returns null if missing or if the id refers to
        /// something that isn't a Component.
        /// </summary>
        public static Component FindComponentById(string instanceId)
        {
            return ObjectIdHelper.ToObject(instanceId) as Component;
        }

        /// <summary>
        /// Build a "/Foo/Bar/Baz" path for a GameObject relative to its scene root.
        /// </summary>
        public static string GetGameObjectPath(GameObject go)
        {
            if (go == null) return string.Empty;
            var t = go.transform;
            var parts = new List<string> { t.name };
            while (t.parent != null)
            {
                t = t.parent;
                parts.Add(t.name);
            }
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
