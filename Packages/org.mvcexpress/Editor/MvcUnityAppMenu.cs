using mvcExpress.Editor.Generators;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor
{
    /// <summary>
    /// Unity editor Menu items for mvcExpress framework.
    /// </summary>
    public static class MvcUnityAppMenu
    {
        [MenuItem("Tools/mvcExpress/Settings", priority = 2000)]
        private static void OpenProjectSettings()
        {
            SettingsService.OpenProjectSettings("Project/mvcExpress");
        }

    }
}
