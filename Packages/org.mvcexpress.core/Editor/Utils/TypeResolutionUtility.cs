using System;
using System.Collections.Generic;
using UnityEngine;

namespace mvcExpress.Editor.Core
{
    internal static class TypeResolutionUtility
    {
        // PERFORMANCE: Cache type resolutions to avoid repeated Type.GetType() calls
        // Type.GetType() with assembly-qualified names is expensive (~50-100ns per call)
        // Cache reduces this to dictionary lookup (~10-20ns)
        private static readonly Dictionary<string, Type> s_typeCache = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static int s_lastCacheFrame = -1;

        public static Type SafeGetType(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName))
                return null;

            // Invalidate cache every frame to catch assembly reloads
            // In editor, assemblies can reload during domain reload or script compilation
            int currentFrame = UnityEditor.EditorApplication.isPlaying 
                ? UnityEngine.Time.frameCount 
                : UnityEditor.EditorApplication.timeSinceStartup.GetHashCode();

            if (s_lastCacheFrame != currentFrame)
            {
                s_typeCache.Clear();
                s_lastCacheFrame = currentFrame;
            }

            // Check cache first
            if (s_typeCache.TryGetValue(assemblyQualifiedName, out Type cachedType))
            {
                return cachedType;
            }

            // Resolve type
            Type type = null;
            try
            {
                type = Type.GetType(assemblyQualifiedName, throwOnError: false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Type resolution failed: " + assemblyQualifiedName + ". " + ex.Message);
                s_typeCache[assemblyQualifiedName] = null; // Cache negative result
                return null;
            }

            if (type == null)
            {
                Debug.LogWarning(
                    "Type not found: " + assemblyQualifiedName + ". Possible causes: assembly moved, obfuscation, or domain reload.");
            }

            // Cache result (including null to avoid repeated warnings)
            s_typeCache[assemblyQualifiedName] = type;
            return type;
        }

        /// <summary>
        /// Clear the type resolution cache. Useful after assembly reload or when types change.
        /// </summary>
        internal static void ClearCache()
        {
            s_typeCache.Clear();
            s_lastCacheFrame = -1;
        }
    }
}
