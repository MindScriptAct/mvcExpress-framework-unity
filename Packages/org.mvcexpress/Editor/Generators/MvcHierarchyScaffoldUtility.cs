using System;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using mvcExpress.Editor.Core;

namespace mvcExpress.Editor.Generators
{
    internal static class MvcHierarchyScaffoldUtility
    {
        private const string PendingGameObjectIdKey = "mvcExpress.PendingGoId";
        private const string PendingGameObjectGlobalIdKey = "mvcExpress.PendingGoGlobalId";
        private const string PendingComponentTypeKey = "mvcExpress.PendingComponentType";
        private const string PendingPostActionKey = "mvcExpress.PendingPostAction";
        private const string PendingProgressIdKey = "mvcExpress.PendingProgressId";

        private static string s_cachedProjectKey;
        
        private static string GetProjectKey()
        {
            if (s_cachedProjectKey != null)
                return s_cachedProjectKey;

            var dataPath = Application.dataPath;
            
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(dataPath));
                var hashString = BitConverter.ToString(hash).Replace("-", "").Substring(0, 8);
                s_cachedProjectKey = hashString;
            }

            return s_cachedProjectKey;
        }
        
        private static string MakeProjectSpecificKey(string baseKey)
        {
            if (string.IsNullOrEmpty(baseKey))
                return baseKey;

            return $"{baseKey}_{GetProjectKey()}";
        }

        internal enum PostAddAction
        {
            None = 0,
            FillModuleContainers = 1,
            AddProxyToModelContainer = 2,
            AddServiceToServicesContainer = 3,
            AddMediatorToViewContainer = 4,
        }

        public static void AddComponentAfterCompile(GameObject go, string componentTypeName, PostAddAction postAction)
        {
            if (go == null || string.IsNullOrWhiteSpace(componentTypeName))
                return;

#if !UNITY_6000_4_OR_NEWER
            EditorPrefs.SetInt(MakeProjectSpecificKey(PendingGameObjectIdKey), go.GetInstanceID());
#endif

            var globalId = GlobalObjectId.GetGlobalObjectIdSlow(go);
            EditorPrefs.SetString(MakeProjectSpecificKey(PendingGameObjectGlobalIdKey), globalId.ToString());

            EditorPrefs.SetString(MakeProjectSpecificKey(PendingComponentTypeKey), componentTypeName);
            EditorPrefs.SetInt(MakeProjectSpecificKey(PendingPostActionKey), (int)postAction);

            // Show progress indicator
            var progressId = Progress.Start("Adding Component", $"Waiting for script compilation: {componentTypeName}");
            EditorPrefs.SetInt(MakeProjectSpecificKey(PendingProgressIdKey), progressId);
            Progress.Report(progressId, 0.5f, "Compiling scripts...");

            // Disable the GameObject to prevent user interaction
            var wasActive = go.activeSelf;
            if (wasActive)
            {
                go.SetActive(false);
                EditorUtility.SetDirty(go);
            }

            AssetDatabase.Refresh();
        }

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            if ((!EditorPrefs.HasKey(MakeProjectSpecificKey(PendingGameObjectGlobalIdKey)) && !EditorPrefs.HasKey(MakeProjectSpecificKey(PendingGameObjectIdKey))) ||
                !EditorPrefs.HasKey(MakeProjectSpecificKey(PendingComponentTypeKey)))
                return;

            var globalIdStr = EditorPrefs.GetString(MakeProjectSpecificKey(PendingGameObjectGlobalIdKey), string.Empty);
            var id = EditorPrefs.GetInt(MakeProjectSpecificKey(PendingGameObjectIdKey), 0);
            var typeName = EditorPrefs.GetString(MakeProjectSpecificKey(PendingComponentTypeKey), string.Empty);
            var postAction = (PostAddAction)EditorPrefs.GetInt(MakeProjectSpecificKey(PendingPostActionKey), 0);
            var progressId = EditorPrefs.GetInt(MakeProjectSpecificKey(PendingProgressIdKey), -1);

            EditorPrefs.DeleteKey(MakeProjectSpecificKey(PendingGameObjectIdKey));
            EditorPrefs.DeleteKey(MakeProjectSpecificKey(PendingGameObjectGlobalIdKey));
            EditorPrefs.DeleteKey(MakeProjectSpecificKey(PendingComponentTypeKey));
            EditorPrefs.DeleteKey(MakeProjectSpecificKey(PendingPostActionKey));
            EditorPrefs.DeleteKey(MakeProjectSpecificKey(PendingProgressIdKey));

            if (string.IsNullOrWhiteSpace(typeName))
            {
                if (progressId >= 0)
                {
                    Progress.Finish(progressId, Progress.Status.Failed);
                }
                return;
            }

            GameObject go = null;
            if (!string.IsNullOrWhiteSpace(globalIdStr) && GlobalObjectId.TryParse(globalIdStr, out var gid))
            {
                go = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid) as GameObject;
            }

            if (go == null)
            {
#if !UNITY_6000_4_OR_NEWER
                if (id != 0)
                {
                    go = EditorUtility.InstanceIDToObject(id) as GameObject;
                }
#endif
            }

            if (go == null)
            {
                if (progressId >= 0)
                {
                    Progress.Finish(progressId, Progress.Status.Failed);
                }
                return;
            }

            // Update progress
            if (progressId >= 0)
            {
                Progress.Report(progressId, 0.8f, "Adding component...");
            }

            var type = FindType(typeName);
            if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type))
            {
                Debug.LogWarning($"[mvcExpress] Could not find component '{typeName}' after compile.");
                
                // Re-enable the GameObject
                go.SetActive(true);
                EditorUtility.SetDirty(go);
                
                if (progressId >= 0)
                {
                    Progress.Finish(progressId, Progress.Status.Failed);
                }
                return;
            }

            var component = Undo.AddComponent(go, type);

            try
            {
                RunPostAction(go, component, postAction);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            // Re-enable the GameObject after component is added
            go.SetActive(true);
            EditorUtility.SetDirty(go);

            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);

            // Finish progress with a message
            if (progressId >= 0)
            {
                Progress.Finish(progressId, Progress.Status.Succeeded);
            }
        }

        private static void RunPostAction(GameObject go, Component component, PostAddAction action)
        {
            switch (action)
            {
                case PostAddAction.None:
                    return;

                case PostAddAction.FillModuleContainers:
                {
                    if (component is MvcModule module)
                    {
                        MvcModuleHierarchyGeneratorWindow.FillModuleContainerReferencesForScaffold(module);
                    }
                    return;
                }

                case PostAddAction.AddProxyToModelContainer:
                {
                    if (component is ProxyBehaviour proxy)
                    {
                        MvcProxyBehaviourGeneratorWindow.TryAddProxyToContainerForScaffold(go.transform, proxy);
                    }
                    return;
                }

                case PostAddAction.AddServiceToServicesContainer:
                {
                    if (component is MonoBehaviour mb && !(mb is ProxyBehaviour))
                    {
                        MvcServiceBehaviourGeneratorWindow.TryAddServiceToContainerForScaffold(go.transform, mb);
                    }
                    return;
                }

                case PostAddAction.AddMediatorToViewContainer:
                {
                    if (component is MediatorBehaviour mediator)
                    {
                        MvcMediatorBehaviourGeneratorWindow.TryAddMediatorToContainerForScaffold(go.transform, mediator);
                    }
                    else
                    {
                        Debug.LogWarning($"[mvcExpress] PostAddAction.AddMediatorToViewContainer was requested but component is not a MediatorBehaviour. Component type: {component?.GetType().Name ?? "null"}");
                    }
                    return;
                }

                default:
                    Debug.LogWarning($"[mvcExpress] Unknown PostAddAction: {action}");
                    return;
            }
        }

        private static Type FindType(string typeName)
        {
            var t = Type.GetType(typeName);
            if (t != null)
                return t;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = asm.GetType(typeName);
                    if (t != null)
                        return t;
                }
                catch
                {
                }
            }
            return null;
        }
    }
}
