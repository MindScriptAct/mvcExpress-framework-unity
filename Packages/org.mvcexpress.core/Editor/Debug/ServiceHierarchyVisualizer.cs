using mvcExpress;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Plugins;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.InternalDebug
{
    /// <summary>
    /// Mirrors ProxyHierarchyVisualizer for code-registered services.
    /// Creates a child GameObject under ServiceRegistryBehaviour (or
    /// GlobalServiceRegistryBehaviour) for each code-only service so it
    /// can be inspected and invoked at runtime.
    /// </summary>
    [InitializeOnLoad]
    internal static class ServiceHierarchyVisualizer
    {
        private static readonly Dictionary<MvcDiContainer, Action<object>> _handlers
            = new Dictionary<MvcDiContainer, Action<object>>();

        private static readonly EventInfo _serviceRegisteredEvent = typeof(MvcDiContainer).GetEvent(
            "ServiceRegisteredForDebug",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static MethodInfo AddHandlerMethod   => _serviceRegisteredEvent?.GetAddMethod(true);
        private static MethodInfo RemoveHandlerMethod => _serviceRegisteredEvent?.GetRemoveMethod(true);

        // See the matching remark in ProxyHierarchyVisualizer.cs - a module registering after the
        // play-mode-entry snapshot used to be caught only by a once-a-second full-scene
        // EditorApplication.update poll. IModuleObserver.OnModuleRegistered already fires exactly
        // when that happens; hooking it directly removes the need for the poll.
        private sealed class ModuleRegistrationObserver : IModuleObserver
        {
            public void OnModuleRegistered(Type moduleType)
            {
                var module = MvcFacade.InstanceOrNull?.GetModule(moduleType);
                if (module != null) HookModule(module);
            }

            public void OnModuleInitialized(Type moduleType) { }
            public void OnModuleUnregistered(Type moduleType) { }
        }

        private static readonly ModuleRegistrationObserver _moduleObserver = new ModuleRegistrationObserver();

        static ServiceHierarchyVisualizer()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.delayCall += () => { if (Application.isPlaying) HookAllModules(); };
            MvcPluginBus.Register(_moduleObserver);
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
                HookAllModules();
            else if (state == PlayModeStateChange.ExitingPlayMode)
                UnhookAll();
        }

        // ── Hooking ───────────────────────────────────────────────────────────

        private static void HookAllModules()
        {
            if (AddHandlerMethod == null) return;

#if UNITY_6000_5_OR_NEWER
            var modules = UnityEngine.Object.FindObjectsByType<MvcModule>(FindObjectsInactive.Include);
#elif UNITY_2022_2_OR_NEWER
#pragma warning disable CS0618 // FindObjectsSortMode: deprecated in 6000.5, required before it
            var modules = UnityEngine.Object.FindObjectsByType<MvcModule>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#pragma warning restore CS0618
#else
            var modules = UnityEngine.Object.FindObjectsOfType<MvcModule>();
#endif
            foreach (var module in modules)
            {
                if (module != null) HookModule(module);
            }

            HookGlobal();
        }

        private static void HookModule(MvcModule module)
        {
            if (module == null) return;
            // Previously only ever reached through HookAllModules' own AddHandlerMethod == null
            // guard; now also called directly from ModuleRegistrationObserver, so this needs its
            // own guard rather than relying on a caller to have checked first.
            if (AddHandlerMethod == null) return;

            var container = GetContainer(module);
            if (container == null) return;

            if (_handlers.ContainsKey(container))
            {
                EnsureSnapshot(module, container);
                return;
            }

            Action<object> handler = svc => OnServiceRegistered(module, svc);
            AddHandlerMethod.Invoke(container, new object[] { handler });
            _handlers[container] = handler;

            EnsureSnapshot(module, container);
        }

        private static void HookGlobal()
        {
            var app = FindFacade();
            if (app == null) return;

            var container = GetGlobalContainer();
            if (container == null) return;

            if (_handlers.ContainsKey(container))
            {
                EnsureGlobalSnapshot(app, container);
                return;
            }

            Action<object> handler = svc => OnGlobalServiceRegistered(app, svc);
            AddHandlerMethod.Invoke(container, new object[] { handler });
            _handlers[container] = handler;

            EnsureGlobalSnapshot(app, container);
        }

        private static void EnsureSnapshot(MvcModule module, MvcDiContainer container)
        {
            try
            {
                var dict = GetLogicObjects(container);
                if (dict == null) return;

                foreach (System.Collections.DictionaryEntry kv in dict)
                {
                    var instance = kv.Value;
                    if (instance == null) continue;
                    if (instance is Proxy || instance is ProxyBehaviour || instance is MonoBehaviour) continue;
                    OnServiceRegistered(module, instance);
                }
            }
            catch { }
        }

        private static void EnsureGlobalSnapshot(MvcFacade app, MvcDiContainer container)
        {
            try
            {
                var dict = GetLogicObjects(container);
                if (dict == null) return;

                foreach (System.Collections.DictionaryEntry kv in dict)
                {
                    var instance = kv.Value;
                    if (instance == null) continue;
                    if (instance is Proxy || instance is ProxyBehaviour || instance is MonoBehaviour) continue;
                    OnGlobalServiceRegistered(app, instance);
                }
            }
            catch { }
        }

        private static void UnhookAll()
        {
            if (RemoveHandlerMethod == null) { _handlers.Clear(); return; }
            foreach (var kvp in _handlers)
                RemoveHandlerMethod.Invoke(kvp.Key, new object[] { kvp.Value });
            _handlers.Clear();
        }

        // ── GO creation ───────────────────────────────────────────────────────

        private static void OnServiceRegistered(MvcModule module, object service)
        {
            if (module == null || service == null || !Application.isPlaying) return;

            var registry = module.GetComponentInChildren<ServiceRegistryBehaviour>(true);
            var parent   = registry != null ? registry.transform : module.transform;
            EnsureServiceDebugGO(parent, service);
        }

        private static void OnGlobalServiceRegistered(MvcFacade app, object service)
        {
            if (app == null || service == null || !Application.isPlaying) return;

            var registry = app.GetComponentInChildren<GlobalServiceRegistryBehaviour>(true);
            var parent   = registry != null ? registry.transform : app.transform;
            EnsureServiceDebugGO(parent, service);
        }

        private static void EnsureServiceDebugGO(Transform parent, object service)
        {
            if (parent == null) return;

            var typeName = service.GetType().Name;
            var existing = parent.Find(typeName);

            GameObject go;
            if (existing == null)
            {
                go = new GameObject(typeName);
                go.transform.SetParent(parent, false);
                go.hideFlags = HideFlags.DontSaveInBuild;
            }
            else
            {
                go = existing.gameObject;
            }

            var dbg = go.GetComponent("ServiceDebugBehaviour");
            if (dbg == null)
            {
                var t = Type.GetType("mvcExpress.Internal.ServiceDebug.ServiceDebugBehaviour, org.mvcexpress.core");
                if (t != null) dbg = go.AddComponent(t);
            }

            if (dbg != null)
                go.SendMessage("SetService", service, SendMessageOptions.DontRequireReceiver);
        }

        // ── Reflection helpers ────────────────────────────────────────────────

        private static MvcDiContainer GetContainer(MvcModule module)
        {
            var prop = typeof(MvcModule).GetProperty("DiContainer", BindingFlags.NonPublic | BindingFlags.Instance);
            return prop?.GetValue(module) as MvcDiContainer;
        }

        private static MvcDiContainer GetGlobalContainer()
        {
            var prop = typeof(MvcFacade).GetProperty("Global", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return prop?.GetValue(null) as MvcDiContainer;
        }

        private static System.Collections.IDictionary GetLogicObjects(MvcDiContainer container)
        {
            var field = typeof(MvcDiContainer).GetField("_logicObjects", BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(container) as System.Collections.IDictionary;
        }

        private static MvcFacade FindFacade()
        {
#if UNITY_2022_2_OR_NEWER
            return UnityEngine.Object.FindAnyObjectByType<MvcFacade>(FindObjectsInactive.Include);
#else
            return UnityEngine.Object.FindObjectOfType<MvcFacade>(true);
#endif
        }
    }
}
