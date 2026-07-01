using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor
{
    /// <summary>
    /// Shared helper for consistent right-edge icon placement in the Hierarchy window.
    /// </summary>
    internal static class MvcHierarchyUtils
    {
        private static System.Type s_hwType;
        private static EditorWindow s_hierarchyWindow;

        /// <summary>
        /// Returns the right-edge x coordinate for icon placement, just inside the
        /// vertical scrollbar. Reads the live window width on every call so icons
        /// track window resizes automatically. Only the window <em>reference</em>
        /// is cached — the width value is always read fresh.
        /// </summary>
        internal static float GetRightEdge(Rect selectionRect)
        {
            if (s_hwType == null)
                s_hwType = typeof(UnityEditor.Editor).Assembly
                    .GetType("UnityEditor.SceneHierarchyWindow");

            // Re-find the window whenever the reference goes null
            // (first call, domain reload, or window was closed and reopened).
            if (s_hierarchyWindow == null && s_hwType != null)
            {
                var wins = Resources.FindObjectsOfTypeAll(s_hwType);
                if (wins.Length > 0) s_hierarchyWindow = wins[0] as EditorWindow;
            }

            if (s_hierarchyWindow != null)
            {
                float scrollbarW = Mathf.Max(13f, GUI.skin.verticalScrollbar.fixedWidth);
                return s_hierarchyWindow.position.width - scrollbarW - 1f;
            }

            return selectionRect.xMax;
        }

        // ── Settings shortcuts ────────────────────────────────────────────────

        internal static bool ShowModuleFacadeIcons =>
            mvcExpress.Editor.Settings.MvcExpressProjectSettings.HierarchyIconsModuleFacade;

        internal static bool ShowRegistryIcons =>
            mvcExpress.Editor.Settings.MvcExpressProjectSettings.HierarchyIconsRegistries;

        internal static bool ShowActorIcons =>
            mvcExpress.Editor.Settings.MvcExpressProjectSettings.HierarchyIconsActors;
    }

    /// <summary>
    /// Draws hierarchy icons for MVC actor components that have no custom editor:
    /// MediatorBehaviour, ProxyBehaviour, ProxyDebugBehaviour, and GameObjects
    /// registered as services in any ServiceRegistryBehaviour.
    /// </summary>
    [InitializeOnLoad]
    internal static class MvcActorHierarchyIcons
    {
        private const string MediatorIconPath = "Packages/org.mvcexpress/Editor/Icons/mvc_mediator_icon.png";
        private const string ProxyIconPath    = "Packages/org.mvcexpress/Editor/Icons/mvc_proxy_icon.png";
        private const string ServiceIconPath  = "Packages/org.mvcexpress/Editor/Icons/mvc_service_icon.png";

        private static Texture2D s_mediatorIcon;
        private static Texture2D s_proxyIcon;
        private static Texture2D s_serviceIcon;

        // Cached lookup for ProxyDebugBehaviour (internal type — cannot reference directly).
        private static Type s_proxyDebugType;
        private static bool s_proxyDebugTypeSearched;

        // Service GO cache: IDs of all GameObjects whose MonoBehaviour is listed in any
        // ServiceRegistryBehaviour or GlobalServiceRegistryBehaviour in the loaded scenes.
        // Rebuilt lazily whenever the hierarchy changes.
#if UNITY_6000_4_OR_NEWER
        private static readonly HashSet<EntityId> s_serviceGoIds = new HashSet<EntityId>();
#else
        private static readonly HashSet<int> s_serviceGoIds = new HashSet<int>();
#endif
        private static bool s_serviceIdsCacheValid;

        static MvcActorHierarchyIcons()
        {
#if UNITY_6000_4_OR_NEWER
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyGUI;
#else
#pragma warning disable CS0618 // hierarchyWindowItemOnGUI: pre-6000.4 API
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
#pragma warning restore CS0618
#endif
            // Invalidate service cache whenever the scene hierarchy or play-mode changes.
            EditorApplication.hierarchyChanged        += InvalidateServiceCache;
            EditorApplication.playModeStateChanged    += _ => InvalidateServiceCache();
        }

        private static void InvalidateServiceCache() => s_serviceIdsCacheValid = false;

        private static void EnsureIcons()
        {
            s_mediatorIcon ??= AssetDatabase.LoadAssetAtPath<Texture2D>(MediatorIconPath);
            s_proxyIcon    ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ProxyIconPath);
            s_serviceIcon  ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ServiceIconPath);
        }

        // Scans all ServiceRegistryBehaviour and GlobalServiceRegistryBehaviour instances
        // in every loaded scene and collects their referenced service GameObjects' instance IDs.
        private static void RebuildServiceCache()
        {
            s_serviceGoIds.Clear();

#if UNITY_6000_5_OR_NEWER
            foreach (var reg in UnityEngine.Object.FindObjectsByType<ServiceRegistryBehaviour>(FindObjectsInactive.Include))
                CollectServiceIds(reg.ServiceMappings);

            foreach (var reg in UnityEngine.Object.FindObjectsByType<GlobalServiceRegistryBehaviour>(FindObjectsInactive.Include))
                CollectServiceIds(reg.ServiceMappings);
#elif UNITY_2023_1_OR_NEWER
#pragma warning disable CS0618 // FindObjectsSortMode: deprecated in 6000.5, required before it
            foreach (var reg in UnityEngine.Object.FindObjectsByType<ServiceRegistryBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                CollectServiceIds(reg.ServiceMappings);

            foreach (var reg in UnityEngine.Object.FindObjectsByType<GlobalServiceRegistryBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                CollectServiceIds(reg.ServiceMappings);
#pragma warning restore CS0618
#else
#pragma warning disable CS0618 // FindObjectsOfType: pre-2023.1 API
            foreach (var reg in UnityEngine.Object.FindObjectsOfType<ServiceRegistryBehaviour>(true))
                CollectServiceIds(reg.ServiceMappings);

            foreach (var reg in UnityEngine.Object.FindObjectsOfType<GlobalServiceRegistryBehaviour>(true))
                CollectServiceIds(reg.ServiceMappings);
#pragma warning restore CS0618
#endif

            s_serviceIdsCacheValid = true;
        }

        private static void CollectServiceIds(mvcExpress.Internal.Services.ServiceMapping[] mappings)
        {
            if (mappings == null) return;
            foreach (var m in mappings)
            {
                if (m?.Service != null)
#if UNITY_6000_4_OR_NEWER
                    s_serviceGoIds.Add(m.Service.gameObject.GetEntityId());
#else
#pragma warning disable CS0618 // GetInstanceID: pre-6000.4 API
                    s_serviceGoIds.Add(m.Service.gameObject.GetInstanceID());
#pragma warning restore CS0618
#endif
            }
        }

        private static Type GetProxyDebugType()
        {
            if (s_proxyDebugTypeSearched) return s_proxyDebugType;
            s_proxyDebugTypeSearched = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType("mvcExpress.Internal.ProxyDebug.ProxyDebugBehaviour");
                    if (t != null) { s_proxyDebugType = t; return t; }
                }
                catch { }
            }
            return null;
        }

        private static void OnHierarchyGUI(
#if UNITY_6000_4_OR_NEWER
            EntityId entityId,
#else
            int instanceID,
#endif
            Rect selectionRect)
        {
            if (!MvcHierarchyUtils.ShowActorIcons) return;
            EnsureIcons();

            // Rebuild service ID cache if stale (lazy, only on demand).
            if (!s_serviceIdsCacheValid) RebuildServiceCache();

            GameObject obj;
#if UNITY_6000_4_OR_NEWER
            obj = EditorUtility.EntityIdToObject(entityId) as GameObject;
#else
#pragma warning disable CS0618 // InstanceIDToObject: pre-6000.4 API
            obj = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
#pragma warning restore CS0618
#endif
            if (obj == null) return;

            Texture2D icon = null;

            if (s_mediatorIcon != null && obj.GetComponent<MediatorBehaviour>() != null)
            {
                icon = s_mediatorIcon;
            }
            else if (s_proxyIcon != null)
            {
                if (obj.GetComponent<ProxyBehaviour>() != null)
                {
                    icon = s_proxyIcon;
                }
                else
                {
                    var debugType = GetProxyDebugType();
                    if (debugType != null && obj.GetComponent(debugType) != null)
                        icon = s_proxyIcon;
                }
            }

            // Service: no common base class — detected by registry membership.
#if UNITY_6000_4_OR_NEWER
            if (icon == null && s_serviceIcon != null && s_serviceGoIds.Contains(obj.GetEntityId()))
#else
#pragma warning disable CS0618 // GetInstanceID: pre-6000.4 API
            if (icon == null && s_serviceIcon != null && s_serviceGoIds.Contains(obj.GetInstanceID()))
#pragma warning restore CS0618
#endif
                icon = s_serviceIcon;

            if (icon != null)
                GUI.DrawTexture(new Rect(MvcHierarchyUtils.GetRightEdge(selectionRect) - 16, selectionRect.y, 16, 16), icon, ScaleMode.ScaleToFit);
        }
    }
}
