using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace mvcExpress
{
    /// <summary>
    /// Serializable startup entry that tells <see cref="MvcFacade"/> how to launch one <see cref="MvcModule"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A list of these entries (typically on the <c>MvcFacade</c> GameObject) drives
    /// <c>MvcFacade.StartConfiguredModules</c>. Entries are spawned in list order.
    /// </para>
    /// <para>
    /// Two launch strategies are supported:
    /// <list type="bullet">
    /// <item><description><see cref="ModulePrefab"/> set - the app instantiates the prefab and starts the module component on it.</description></item>
    /// <item><description><see cref="ModuleTypeName"/> set (no prefab) - the app creates the module entirely in code.</description></item>
    /// </list>
    /// Set <see cref="AutoStart"/> to <c>false</c> to register the entry without starting it automatically,
    /// then start it later by calling <see cref="MvcFacade.SpawnModule(Type)"/> directly.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class MvcStartupModuleEntry
    {
        /// <summary>
        /// When true, <see cref="MvcFacade.StartConfiguredModules"/> starts this entry automatically.
        /// </summary>
        [SerializeField]
        public bool AutoStart = true;

        [SerializeField]
        [UnityEngine.Serialization.FormerlySerializedAs("ModuleTypeName")]
        private string _moduleTypeName;

#if UNITY_EDITOR
        [SerializeField]
        [Tooltip("Editor-only script reference used to keep ModuleTypeName stable across renames.")]
        public MonoScript ModuleScript;
#endif

        /// <summary>
        /// Optional prefab whose root contains the configured module component.
        /// </summary>
        [SerializeField]
        public GameObject ModulePrefab;

        [NonSerialized]
        private Type _moduleType;

        /// <summary>
        /// Assembly-qualified module type name used to resolve the module at runtime.
        /// </summary>
        public string ModuleTypeName => _moduleTypeName;

        /// <summary>
        /// Creates an entry for a module identified by its assembly-qualified type name.
        /// </summary>
        public static MvcStartupModuleEntry ForType(string assemblyQualifiedTypeName, bool autoStart = true)
        {
            var entry = new MvcStartupModuleEntry();
            entry._moduleTypeName = assemblyQualifiedTypeName;
            entry.AutoStart = autoStart;
            return entry;
        }

        /// <summary>
        /// Creates an entry for the given module type.
        /// </summary>
        public static MvcStartupModuleEntry ForType(Type moduleType, bool autoStart = true)
        {
            return ForType(moduleType?.AssemblyQualifiedName, autoStart);
        }

        /// <summary>
        /// Creates an entry for the given module type.
        /// </summary>
        public static MvcStartupModuleEntry ForType<T>(bool autoStart = true) where T : MvcModule
        {
            return ForType(typeof(T).AssemblyQualifiedName, autoStart);
        }

        /// <summary>
        /// Resolves the configured module type from the prefab or stored type name.
        /// </summary>
        /// <returns>The resolved module type, or null when the entry is incomplete.</returns>
        public Type ResolveModuleType()
        {
            if (_moduleType != null)
                return _moduleType;

            if (ModulePrefab != null)
            {
                var module = ModulePrefab.GetComponent<MvcModule>();
                if (module != null)
                {
                    _moduleType = module.ModuleType;
                    return _moduleType;
                }
            }

            if (!string.IsNullOrWhiteSpace(_moduleTypeName))
            {
                _moduleType = Type.GetType(_moduleTypeName, throwOnError: false);
            }

            return _moduleType;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Synchronizes serialized type data from editor-only object references.
        /// </summary>
        internal void EditorSyncTypeNameFromReferences()
        {
            if (ModulePrefab != null)
            {
                var module = ModulePrefab.GetComponent<MvcModule>();
                if (module != null)
                {
                    _moduleTypeName = module.ModuleType.AssemblyQualifiedName;
                    ModuleScript = MonoScript.FromMonoBehaviour(module);
                    _moduleType = null;
                    return;
                }
            }

            if (ModuleScript != null)
            {
                var type = ModuleScript.GetClass();
                if (type != null && typeof(MvcModule).IsAssignableFrom(type))
                {
                    _moduleTypeName = type.AssemblyQualifiedName;
                    _moduleType = null;
                }
            }
        }
#endif

        /// <summary>
        /// Returns whether this entry can launch a concrete module type.
        /// </summary>
        public bool IsValid()
        {
            var moduleType = ResolveModuleType();
            return moduleType != null
                && typeof(MvcModule).IsAssignableFrom(moduleType)
                && !moduleType.IsAbstract;
        }
    }
}
