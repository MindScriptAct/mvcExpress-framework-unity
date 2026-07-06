using System;
using UnityEngine;

namespace mvcExpress.Internal.Initialization
{
    /// <summary>
    /// Outcome of resolving a MonoBehaviour-derived type to a scene instance.
    /// </summary>
    internal enum MonoBehaviourResolutionKind
    {
        /// <summary>An existing instance was reused (tracked mapping or hierarchy scan).</summary>
        Found,

        /// <summary>No existing instance was found; a new one was created under the parent container.</summary>
        Created,

        /// <summary>More than one instance of the type exists in the searched hierarchy.</summary>
        Ambiguous
    }

    /// <summary>
    /// Result of <see cref="AttributeMonoBehaviourResolver.Resolve"/>.
    /// </summary>
    internal readonly struct MonoBehaviourResolution
    {
        public readonly MonoBehaviourResolutionKind Kind;
        public readonly MonoBehaviour Instance;
        public readonly GameObject[] Conflicts;

        private MonoBehaviourResolution(MonoBehaviourResolutionKind kind, MonoBehaviour instance, GameObject[] conflicts)
        {
            Kind = kind;
            Instance = instance;
            Conflicts = conflicts;
        }

        public static MonoBehaviourResolution Found(MonoBehaviour instance) =>
            new MonoBehaviourResolution(MonoBehaviourResolutionKind.Found, instance, null);

        public static MonoBehaviourResolution Created(MonoBehaviour instance) =>
            new MonoBehaviourResolution(MonoBehaviourResolutionKind.Created, instance, null);

        public static MonoBehaviourResolution Ambiguous(GameObject[] conflicts) =>
            new MonoBehaviourResolution(MonoBehaviourResolutionKind.Ambiguous, null, conflicts);
    }

    /// <summary>
    /// Resolves a MonoBehaviour-derived type marked with <c>[Register]</c>/<c>[RegisterGlobal]</c> to a
    /// single scene instance: reuses one already tracked via Inspector mapping, finds one hand-placed
    /// anywhere in the searched hierarchy, or creates one under a container transform when none exists.
    /// </summary>
    /// <remarks>
    /// Shared by module-scoped ProxyBehaviour/Service attribute registration (<see cref="ModuleInitializer"/>)
    /// and global ProxyBehaviour/Service attribute registration (<see cref="MvcFacade"/>). This class owns
    /// only Unity-hierarchy plumbing - it has no knowledge of DI registration, lifecycle scopes, or which
    /// flavor (Proxy vs. Service, module vs. global) is calling it. Callers own registration and logging,
    /// since only they have the context (module name, scope) needed for useful messages.
    /// Internal - not part of the public API.
    /// </remarks>
    internal static class AttributeMonoBehaviourResolver
    {
        /// <summary>
        /// Resolves a single instance of <paramref name="behaviourType"/> under <paramref name="searchRoot"/>.
        /// </summary>
        /// <param name="searchRoot">Root transform to search (the owning module's or facade's own transform).</param>
        /// <param name="behaviourType">Concrete MonoBehaviour-derived type to resolve.</param>
        /// <param name="parentContainer">Container a newly created instance is parented under. Must be non-null; callers are expected to have already ensured their containers exist.</param>
        /// <param name="preTracked">
        /// An instance already known via Inspector-driven mapping (<c>ProxyMapping</c>/<c>ServiceMapping</c>), if any.
        /// When non-null, this short-circuits the hierarchy scan entirely - an explicitly wired instance always wins.
        /// </param>
        internal static MonoBehaviourResolution Resolve(
            Transform searchRoot,
            Type behaviourType,
            Transform parentContainer,
            MonoBehaviour preTracked)
        {
            if (preTracked != null)
                return MonoBehaviourResolution.Found(preTracked);

            var matches = searchRoot.GetComponentsInChildren(behaviourType, includeInactive: true);

            if (matches.Length == 1)
                return MonoBehaviourResolution.Found((MonoBehaviour)matches[0]);

            if (matches.Length > 1)
            {
                var conflicts = new GameObject[matches.Length];
                for (int i = 0; i < matches.Length; i++)
                    conflicts[i] = matches[i].gameObject;

                return MonoBehaviourResolution.Ambiguous(conflicts);
            }

            var go = new GameObject(behaviourType.Name);
            go.transform.SetParent(parentContainer, false);
            var created = (MonoBehaviour)go.AddComponent(behaviourType);

            return MonoBehaviourResolution.Created(created);
        }
    }
}
