using System;
using System.IO;
using UnityEngine;

namespace mvcExpress.Editor.Generators
{
    /// <summary>
    /// Resolves the target C# namespace for a generated script from its asset path, generator settings, and any custom-namespace override.
    /// </summary>
    internal static class MvcNamespaceUtility
    {
        public static string Resolve(string assetPath, bool useNamespace, bool useCustomNamespace, string customNamespace, string defaultNamespace, int skipFolderLevels)
        {
            if (!useNamespace)
                return null;

            if (useCustomNamespace)
                return customNamespace;

            assetPath = assetPath.Replace("\\", "/");

            var dir = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(dir))
                return defaultNamespace;

            var parts = dir.Split('/');
            int start = Mathf.Clamp(skipFolderLevels, 0, parts.Length);
            if (start >= parts.Length)
                return defaultNamespace;

            var tail = string.Join(".", parts, start, parts.Length - start);

            if (string.IsNullOrWhiteSpace(defaultNamespace))
                return tail;

            if (string.IsNullOrWhiteSpace(tail))
                return defaultNamespace;

            return defaultNamespace + "." + tail;
        }
    }
}
