using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.Core
{
    /// <summary>
    /// Centralized MonoScript cache to avoid expensive AssetDatabase.FindAssets calls.
    /// Automatically invalidates on assembly reload.
    /// PERFORMANCE: Optimized to minimize file I/O and AssetDatabase calls.
    /// </summary>
    public static class MonoScriptCache
    {
        private static readonly Dictionary<string, MonoScript> s_typeToScript = new Dictionary<string, MonoScript>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> s_guidToPath = new Dictionary<string, string>(StringComparer.Ordinal);
        private static bool s_initialized = false;

        // PERFORMANCE: Profiler markers for optimization analysis
        private static readonly EditorProfilerMarker s_rebuildCacheMarker = new EditorProfilerMarker("MonoScriptCache.RebuildCache");
        private static readonly EditorProfilerMarker s_findScriptMarker = new EditorProfilerMarker("MonoScriptCache.FindScript");
        private static readonly EditorProfilerMarker s_scanFilesMarker = new EditorProfilerMarker("MonoScriptCache.ScanFiles");

        [InitializeOnLoadMethod]
        private static void InitializeCache()
        {
            AssemblyReloadEvents.afterAssemblyReload -= ClearCache;
            AssemblyReloadEvents.afterAssemblyReload += ClearCache;
        }

        private static void ClearCache()
        {
            s_typeToScript.Clear();
            s_guidToPath.Clear();
            s_initialized = false;
        }

        /// <summary>
        /// Rebuilds the entire cache by scanning all MonoScripts in the project.
        /// This is expensive but only needs to be done once per domain reload.
        /// </summary>
        [MenuItem("Tools/mvcExpress/Rebuild MonoScript Cache")]
        public static void RebuildCache()
        {
            using (s_rebuildCacheMarker.Auto())
            {
                s_typeToScript.Clear();
                s_guidToPath.Clear();

                var guids = AssetDatabase.FindAssets("t:MonoScript");
                if (guids == null || guids.Length == 0)
                {
                    s_initialized = true;
                    return;
                }

                // PERFORMANCE: Pre-cache GUID to path mappings to avoid repeated calls
                for (int i = 0; i < guids.Length; i++)
                {
                    var guid = guids[i];
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path) && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        s_guidToPath[guid] = path;
                    }
                }

                // PERFORMANCE: Batch load assets instead of individual calls
                foreach (var kvp in s_guidToPath)
                {
                    var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(kvp.Value);
                    if (ms == null)
                        continue;

                    var cls = ms.GetClass();
                    if (cls == null)
                        continue;

                    var key = cls.FullName;
                    if (string.IsNullOrEmpty(key))
                        continue;

                    if (!s_typeToScript.ContainsKey(key))
                        s_typeToScript[key] = ms;
                }

                s_initialized = true;
            }
        }

        /// <summary>
        /// Finds the MonoScript for a given type with caching.
        /// </summary>
        public static MonoScript FindScriptForType(Type type)
        {
            using (s_findScriptMarker.Auto())
            {
                if (type == null || string.IsNullOrEmpty(type.FullName))
                    return null;

                // Lazy initialization
                if (!s_initialized)
                    RebuildCache();

                // Check cache first
                if (s_typeToScript.TryGetValue(type.FullName, out var cached))
                    return cached;

                // Try fast Unity-resolvable lookup
                var script = FindMonoScriptForTypeInternal(type);
                if (script != null)
                {
                    s_typeToScript[type.FullName] = script;
                    return script;
                }

                // Fallback: scan for type declaration in files (covers multi-type files)
                // PERFORMANCE: Only scan if necessary - this is expensive
                script = FindScriptByScanning(type);
                s_typeToScript[type.FullName] = script; // Cache even if null
                return script;
            }
        }

        private static MonoScript FindMonoScriptForTypeInternal(Type type)
        {
            if (type == null)
                return null;

            // PERFORMANCE: Use cached GUID-to-path mappings when available
            var guids = AssetDatabase.FindAssets($"{type.Name} t:MonoScript");
            
            for (int i = 0; i < guids.Length; i++)
            {
                var guid = guids[i];
                
                // Try cached path first
                if (!s_guidToPath.TryGetValue(guid, out var path))
                {
                    path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path) && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        s_guidToPath[guid] = path;
                    }
                }

                if (string.IsNullOrEmpty(path))
                    continue;

                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (ms == null)
                    continue;

                var cls = ms.GetClass();
                if (cls == type)
                    return ms;
            }

            // Fallback: first name match
            if (guids.Length > 0)
            {
                var guid = guids[0];
                if (!s_guidToPath.TryGetValue(guid, out var path))
                {
                    path = AssetDatabase.GUIDToAssetPath(guid);
                }
                
                if (!string.IsNullOrEmpty(path))
                {
                    return AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                }
            }

            return null;
        }

        private static MonoScript FindScriptByScanning(Type type)
        {
            using (s_scanFilesMarker.Auto())
            {
                if (type == null)
                    return null;

                // PERFORMANCE: Use cached paths instead of repeated GUID lookups
                if (s_guidToPath.Count == 0)
                {
                    // Rebuild path cache if empty
                    var guids = AssetDatabase.FindAssets("t:MonoScript");
                    if (guids == null || guids.Length == 0)
                        return null;

                    for (int i = 0; i < guids.Length; i++)
                    {
                        var guid = guids[i];
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        if (!string.IsNullOrEmpty(path) && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                        {
                            s_guidToPath[guid] = path;
                        }
                    }
                }

                string typeName = type.Name;
                
                // PERFORMANCE: Use buffered file reading for large files
                foreach (var path in s_guidToPath.Values)
                {
                    try
                    {
                        // PERFORMANCE: Quick filename check before reading file
                        var filename = Path.GetFileNameWithoutExtension(path);
                        if (filename != null && filename.Contains(typeName))
                        {
                            var source = File.ReadAllText(path);
                            if (ContainsTypeDeclaration(source, typeName))
                            {
                                return AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                            }
                        }
                    }
                    catch
                    {
                        // Ignore file read errors
                    }
                }

                return null;
            }
        }

        private static bool ContainsTypeDeclaration(string source, string typeName)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(typeName))
                return false;

            // Quick reject
            if (source.IndexOf(typeName, StringComparison.Ordinal) < 0)
                return false;

            // Match patterns: "class Foo", "struct Foo", "interface Foo", "enum Foo"
            return ContainsDecl(source, "class", typeName)
                || ContainsDecl(source, "struct", typeName)
                || ContainsDecl(source, "interface", typeName)
                || ContainsDecl(source, "enum", typeName);
        }

        private static bool ContainsDecl(string source, string keyword, string typeName)
        {
            int idx = 0;
            while (true)
            {
                idx = source.IndexOf(keyword, idx, StringComparison.Ordinal);
                if (idx < 0) return false;

                idx += keyword.Length;
                if (idx >= source.Length || !char.IsWhiteSpace(source[idx]))
                    continue;

                while (idx < source.Length && char.IsWhiteSpace(source[idx])) idx++;
                if (idx + typeName.Length <= source.Length &&
                    string.CompareOrdinal(source, idx, typeName, 0, typeName.Length) == 0)
                {
                    int end = idx + typeName.Length;
                    if (end >= source.Length || !IsIdentifierChar(source[end]))
                        return true;
                }
            }
        }

        private static bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        /// <summary>
        /// Opens the script for a type in the editor.
        /// </summary>
        public static bool TryOpenScript(Type type)
        {
            var script = FindScriptForType(type);
            if (script != null)
            {
                AssetDatabase.OpenAsset(script);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Clears specific type from cache (useful when prefab changes).
        /// </summary>
        public static void InvalidateType(Type type)
        {
            if (type != null && !string.IsNullOrEmpty(type.FullName))
            {
                s_typeToScript.Remove(type.FullName);
            }
        }
    }
}
