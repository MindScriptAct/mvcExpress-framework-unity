using mvcExpress;
using mvcExpress.Internal.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.InternalDebug
{
    [InitializeOnLoad]
    internal static class ProxyHierarchyVisualizer
    {
        private const string ModuleModelRootName = "Model";
        private const string GlobalModelRootName = "Static"; // widely used concept; formerly "GlobalModel"

        private static readonly Dictionary<MvcDiContainer, Action<object>> _handlers = new Dictionary<MvcDiContainer, Action<object>>();
        private static readonly EventInfo _proxyRegisteredEvent = typeof(MvcDiContainer).GetEvent(
            "ProxyRegisteredForDebug",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static MethodInfo AddHandlerMethod => _proxyRegisteredEvent?.GetAddMethod(true);
        private static MethodInfo RemoveHandlerMethod => _proxyRegisteredEvent?.GetRemoveMethod(true);

        static ProxyHierarchyVisualizer()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.delayCall += () =>
            {
                if (Application.isPlaying)
                {
                    HookAllModules();
                }
            };
            EditorApplication.update += OnEditorUpdate;
        }

        private static double _nextUpdate;

        private static void OnEditorUpdate()
        {
            if (!Application.isPlaying) return;

            // TEMP: periodically ensure hooks exist (covers proxies registered very early when entering play mode)
            if (EditorApplication.timeSinceStartup < _nextUpdate) return;
            _nextUpdate = EditorApplication.timeSinceStartup + 1.0d;

            HookAllModules();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                HookAllModules();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                UnhookAll();
            }
        }

        private static void HookAllModules()
        {
#if UNITY_6000_5_OR_NEWER
            var modules = UnityEngine.Object.FindObjectsByType<MvcModule>();
#elif UNITY_2022_2_OR_NEWER
#pragma warning disable CS0618 // FindObjectsSortMode: deprecated in 6000.5, required before it
            var modules = UnityEngine.Object.FindObjectsByType<MvcModule>(FindObjectsSortMode.None);
#pragma warning restore CS0618
#else
            var modules = UnityEngine.Object.FindObjectsOfType<MvcModule>();
#endif
            foreach (var module in modules)
            {
                if (module == null) continue;
                HookModule(module);
            }

            HookGlobal();
        }

        private static void HookGlobal()
        {
#if UNITY_2022_2_OR_NEWER
            var app = UnityEngine.Object.FindAnyObjectByType<MvcFacade>(FindObjectsInactive.Include);
#else
            var app = UnityEngine.Object.FindObjectOfType<MvcFacade>(true);
#endif
            if (app == null) return;

            var container = GetGlobalContainer();
            if (container == null) return;

            if (_handlers.TryGetValue(container, out var existing))
            {
                EnsureGlobalSnapshot(app, container);
                return;
            }

            Action<object> handler = proxy => OnGlobalProxyRegistered(app, proxy);
            if (_proxyRegisteredEvent != null && AddHandlerMethod != null)
            {
                AddHandlerMethod.Invoke(container, new object[] { handler });
                _handlers[container] = handler;
            }

            EnsureGlobalSnapshot(app, container);
        }

        private static MvcDiContainer GetGlobalContainer()
        {
            // Global is not public; use reflection (editor-only).
            var prop = typeof(MvcFacade).GetProperty("Global", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return prop?.GetValue(null) as MvcDiContainer;
        }

        private static void EnsureGlobalSnapshot(MvcFacade app, MvcDiContainer container)
        {
            try
            {
                var dictField = typeof(MvcDiContainer).GetField("_logicObjects", BindingFlags.NonPublic | BindingFlags.Instance);
                var dict = dictField?.GetValue(container) as System.Collections.IDictionary;
                if (dict == null) return;

                foreach (System.Collections.DictionaryEntry kv in dict)
                {
                    var instance = kv.Value;
                    if (instance == null) continue;

                    if (instance is ProxyBehaviour || instance is mvcExpress.Proxy)
                    {
                        OnGlobalProxyRegistered(app, instance);
                    }
                }
            }
            catch { }
        }

        private static void OnGlobalProxyRegistered(MvcFacade app, object proxy)
        {
            if (app == null || proxy == null) return;
            if (!Application.isPlaying) return;

            var root = EnsureRoot(app.transform, GlobalModelRootName);

            if (proxy is ProxyBehaviour pb)
            {
                EnsureBehaviourProxyGO(root, pb);
            }
            else
            {
                EnsureCodeProxyGO(root, proxy);
            }
        }

        private static Transform EnsureModelRoot(Transform moduleTransform)
        {
            return EnsureRoot(moduleTransform, ModuleModelRootName);
        }

        private static Transform EnsureRoot(Transform ownerTransform, string rootName)
        {
            if (ownerTransform == null) return null;

            var model = ownerTransform.Find(rootName);
            if (model != null) return model;

            var go = new GameObject(rootName);
            go.transform.SetParent(ownerTransform, false);
            go.hideFlags = HideFlags.DontSaveInBuild;
            return go.transform;
        }

        private static void UnhookAll()
        {
            if (_proxyRegisteredEvent == null || RemoveHandlerMethod == null)
            {
                _handlers.Clear();
                return;
            }

            foreach (var kvp in _handlers)
            {
                RemoveHandlerMethod.Invoke(kvp.Key, new object[] { kvp.Value });
            }
            _handlers.Clear();
        }

        private static void HookModule(MvcModule module)
        {
            if (module == null) return;
            if (_proxyRegisteredEvent == null || AddHandlerMethod == null || RemoveHandlerMethod == null)
            {
                return;
            }

            var container = GetContainer(module);
            if (container == null)
            {
                return;
            }

            if (_handlers.TryGetValue(container, out var existing))
            {
                // Already hooked, but still ensure snapshot exists.
                EnsureSnapshot(module, container);
                return;
            }

            Action<object> handler = proxy => OnProxyRegistered(module, proxy);
            AddHandlerMethod.Invoke(container, new object[] { handler });
            _handlers[container] = handler;

            EnsureSnapshot(module, container);
        }

        private static void EnsureSnapshot(MvcModule module, MvcDiContainer container)
        {
            try
            {
                var dictField = typeof(MvcDiContainer).GetField("_logicObjects", BindingFlags.NonPublic | BindingFlags.Instance);
                var dict = dictField?.GetValue(container) as System.Collections.IDictionary;

                if (dict == null)
                {
                    return;
                }

                int count = 0;
                foreach (System.Collections.DictionaryEntry kv in dict)
                {
                    var instance = kv.Value;
                    if (instance == null) continue;

                    if (instance is ProxyBehaviour || instance is mvcExpress.Proxy)
                    {
                        count++;
                        OnProxyRegistered(module, instance);
                    }
                }

            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[ProxyHierarchyVisualizer] Snapshot failed: {ex.Message}");
            }
        }

        private static MvcDiContainer GetContainer(MvcModule module)
        {
            var prop = typeof(MvcModule).GetProperty("DiContainer", BindingFlags.NonPublic | BindingFlags.Instance);
            return prop?.GetValue(module) as MvcDiContainer;
        }

        private static void OnProxyRegistered(MvcModule module, object proxy)
        {
            if (module == null || proxy == null) return;
            if (!Application.isPlaying) return;

            var modelRoot = EnsureModelRoot(module.transform);

            if (proxy is ProxyBehaviour pb)
            {
                EnsureBehaviourProxyGO(modelRoot, pb);
            }
            else
            {
                EnsureCodeProxyGO(modelRoot, proxy);
            }
        }

        private static void EnsureBehaviourProxyGO(Transform modelRoot, ProxyBehaviour proxy)
        {
            if (proxy == null) return;

            if (proxy.transform.parent != modelRoot)
            {
                proxy.transform.SetParent(modelRoot, false);
            }
        }

        private static void EnsureCodeProxyGO(Transform modelRoot, object proxy)
        {
            var typeName = proxy.GetType().Name;
            var existing = modelRoot.Find(typeName);

            GameObject go;
            Component dbg;

            if (existing == null)
            {
                go = new GameObject(typeName);
                go.transform.SetParent(modelRoot, false);
                go.hideFlags = HideFlags.DontSaveInBuild;
            }
            else
            {
                go = existing.gameObject;
            }

            dbg = go.GetComponent("ProxyDebugBehaviour");
            if (dbg == null)
            {
                var t = Type.GetType("mvcExpress.Internal.ProxyDebug.ProxyDebugBehaviour, org.mvcexpress");
                if (t != null)
                {
                    dbg = go.AddComponent(t);
                }
            }

            if (dbg != null)
            {
                go.SendMessage("SetProxy", proxy, SendMessageOptions.DontRequireReceiver);
            }
            else
            {
                UnityEngine.Debug.LogWarning($"[ProxyHierarchyVisualizer] Failed to add ProxyDebugBehaviour to GO '{go.name}'");
            }
        }
    }
}
