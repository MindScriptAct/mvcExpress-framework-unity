using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace mvcExpress
{
    /// <summary>
    /// Inspector-friendly registry for message-to-command bindings owned by a module.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the Unity-behaviour binding method for Commands - the first of three methods
    /// (Inspector → Attribute → Code). Add this component as a child of your <see cref="MvcModule"/>
    /// GameObject and configure <see cref="CommandBindings"/> in the Inspector.
    /// The module initializer processes these bindings during <c>BindCommands</c>, before any
    /// <c>[Bind]</c> attributes or code bindings.
    /// </para>
    /// <para>
    /// Use this approach when: you want non-programmers to configure which commands respond to
    /// which messages, or when you need to swap command implementations between scenes without
    /// recompiling.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CommandBindingsBehaviour : MonoBehaviour
    {
        [SerializeField]
        private CommandBindingMapping[] _commandBindings = Array.Empty<CommandBindingMapping>();

        /// <summary>
        /// Command bindings configured for the owning module.
        /// </summary>
        public CommandBindingMapping[] CommandBindings => _commandBindings;
    }

    /// <summary>
    /// Serializable Inspector mapping between one message type and one command type.
    /// </summary>
    [Serializable]
    public sealed class CommandBindingMapping
    {
#if UNITY_EDITOR
        /// <summary>Editor-only script reference; keeps <see cref="CommandTypeName"/> stable across renames.</summary>
        [SerializeField] public MonoScript CommandScript;
        /// <summary>Editor-only script reference; keeps <see cref="MessageTypeName"/> stable across renames.</summary>
        [SerializeField] public MonoScript MessageScript;
#endif
        /// <summary>Assembly-qualified command type name resolved at runtime.</summary>
        [SerializeField] public string CommandTypeName;
        /// <summary>Assembly-qualified message type name that triggers the command.</summary>
        [SerializeField] public string MessageTypeName;
        /// <summary>When true the binding targets the async command execution path.</summary>
        [SerializeField] public bool IsAsync;
        /// <summary>Object pool size for the command type. Zero disables pooling.</summary>
        [SerializeField] public int PoolSize;
    }
}
