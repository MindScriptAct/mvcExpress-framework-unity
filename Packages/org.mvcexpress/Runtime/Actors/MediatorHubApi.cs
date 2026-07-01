using System;
using System.Runtime.CompilerServices;
using mvcExpress.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace mvcExpress
{
    /// <summary>
    /// Runtime mediator attachment facade exposed to both <see cref="MvcModule"/> and
    /// <see cref="MediatorBehaviour"/> for dynamic view composition.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The framework's static <c>AttachMediators()</c> phase covers mediators that are known at
    /// module startup. Use <see cref="MediatorHubApi"/> for everything that must be created or
    /// destroyed at runtime: spawning a popup mediator on demand, removing a HUD element, or
    /// letting one mediator spawn a child mediator.
    /// </para>
    /// <para>
    /// Dynamic attachment routes through the module so that DI injection and subscription
    /// tracking are applied consistently, regardless of whether the call originates from the
    /// module or from another mediator.
    /// </para>
    /// <para>
    /// Prefab attachment requires a prefab mapping registered on the module (via the module's
    /// prefab map or equivalent). Scene-instance attachment (<see cref="Attach"/>) accepts any
    /// existing <see cref="MediatorBehaviour"/> GameObject in the scene.
    /// </para>
    /// </remarks>
    public readonly struct MediatorHubApi
    {
        private readonly MediatorBehaviour _mediator;
        private readonly MvcModule _module;

        // Module-originated path: constructed when a MvcModule exposes MediatorHub.
        internal MediatorHubApi(MediatorBehaviour mediator)
        {
            _mediator = mediator;
            _module = null;
        }

        // Mediator-originated path: constructed when a MediatorBehaviour exposes MediatorHub.
        internal MediatorHubApi(MvcModule module)
        {
            _mediator = null;
            _module = module;
        }

        /// <summary>
        /// Attaches an existing scene mediator instance to the owning module, injecting its
        /// dependencies and registering it for lifecycle tracking.
        /// </summary>
        /// <param name="mediator">Scene-resident <see cref="MediatorBehaviour"/> to initialize and track.</param>
        /// <returns><c>true</c> when the module accepted and initialized the mediator.</returns>
        /// <remarks>
        /// The mediator must already exist in the scene (e.g. found via <c>GetComponent</c>).
        /// To create a new mediator from a prefab mapping, use <see cref="AttachPrefab{TMediator}()"/> instead.
        /// </remarks>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Attach(MediatorBehaviour mediator)
        {
            if (mediator == null)
            {
                MvcDebug.LogWarning($"Cannot attach null mediator from '{CallerName}'.");
                return false;
            }

            WarnForCodeAttachment(mediator.GetType());
            if (!HasModule($"attach mediator '{mediator.name}'"))
                return false;
            bool attached = Module.AttachMediator(mediator);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (attached)
                MvcPluginBus.FireMediatorAttached(mediator.GetType(), Module?.GetType(), MvcLogContext.RegistrationSource.Code);
#endif
            return attached;
        }
#endif

        /// <summary>
        /// Instantiates and attaches a mapped mediator prefab under the module's default view container.
        /// </summary>
        /// <typeparam name="TMediator">Mediator type used to locate the prefab mapping.</typeparam>
        /// <returns>True when the prefab was instantiated and attached.</returns>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AttachPrefab<TMediator>() where TMediator : MediatorBehaviour
        {
            WarnForCodeAttachment(typeof(TMediator));
            return HasModule("attach mediator prefab") && Module.AttachPrefabMediator<TMediator>();
        }
#endif

        /// <summary>
        /// Instantiates and attaches a mapped mediator prefab under a specific parent.
        /// </summary>
        /// <typeparam name="TMediator">Mediator type used to locate the prefab mapping.</typeparam>
        /// <param name="parent">Transform that receives the instantiated prefab.</param>
        /// <returns>True when the prefab was instantiated and attached.</returns>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AttachPrefab<TMediator>(Transform parent) where TMediator : MediatorBehaviour
        {
            WarnForCodeAttachment(typeof(TMediator));
            return HasModule("attach mediator prefab") && Module.AttachPrefabMediator<TMediator>(parent);
        }
#endif

        /// <summary>
        /// Instantiates and attaches a mapped mediator prefab under a specific parent.
        /// </summary>
        /// <param name="mediatorType">Mediator type used to locate the prefab mapping.</param>
        /// <param name="parent">Transform that receives the instantiated prefab.</param>
        /// <returns>True when the prefab was instantiated and attached.</returns>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AttachPrefab(Type mediatorType, Transform parent)
        {
            WarnForCodeAttachment(mediatorType);
            return HasModule("attach mediator prefab") && Module.AttachPrefabMediator(mediatorType, parent);
        }
#endif

        /// <summary>
        /// Instantiates and attaches a mapped mediator prefab into a specific scene.
        /// </summary>
        /// <typeparam name="TMediator">Mediator type used to locate the prefab mapping.</typeparam>
        /// <param name="scene">Scene that receives the instantiated prefab.</param>
        /// <returns>True when the prefab was instantiated and attached.</returns>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AttachPrefab<TMediator>(Scene scene) where TMediator : MediatorBehaviour
        {
            WarnForCodeAttachment(typeof(TMediator));
            return HasModule("attach mediator prefab") && Module.AttachPrefabMediator<TMediator>(scene);
        }
#endif

        /// <summary>
        /// Instantiates and attaches a mapped mediator prefab into a specific scene.
        /// </summary>
        /// <param name="mediatorType">Mediator type used to locate the prefab mapping.</param>
        /// <param name="scene">Scene that receives the instantiated prefab.</param>
        /// <returns>True when the prefab was instantiated and attached.</returns>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AttachPrefab(Type mediatorType, Scene scene)
        {
            WarnForCodeAttachment(mediatorType);
            return HasModule("attach mediator prefab") && Module.AttachPrefabMediator(mediatorType, scene);
        }
#endif

        /// <summary>
        /// Detaches a runtime-attached mediator instance from the owning module.
        /// </summary>
        /// <param name="mediator">Mediator instance to remove.</param>
        /// <returns>True when the mediator was found and cleaned up.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Detach(MediatorBehaviour mediator)
        {
            if (mediator == null)
            {
                MvcDebug.LogWarning($"Cannot detach null mediator from '{CallerName}'.");
                return false;
            }

            return HasModule($"detach mediator '{mediator.name}'") && Module.DetachMediator(mediator);
        }

        /// <summary>
        /// Detaches the first runtime-attached mediator assignable to <typeparamref name="TMediator"/>.
        /// </summary>
        /// <typeparam name="TMediator">Mediator type to remove.</typeparam>
        /// <returns>True when a matching mediator was found and cleaned up.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Detach<TMediator>() where TMediator : MediatorBehaviour
        {
            return HasModule("detach mediator type") && Module.DetachMediator<TMediator>();
        }

        /// <summary>
        /// Detaches the first runtime-attached mediator assignable to the supplied type.
        /// </summary>
        /// <param name="mediatorType">Mediator type to remove.</param>
        /// <returns>True when a matching mediator was found and cleaned up.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Detach(Type mediatorType)
        {
            return HasModule("detach mediator type") && Module.DetachMediator(mediatorType);
        }

        /// <summary>
        /// Detaches all runtime-attached mediators from the owning module.
        /// </summary>
        /// <returns>True when all runtime mediators were removed or none were attached.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool DetachAll()
        {
            return HasModule("detach all mediators") && Module.DetachAllMediators();
        }

        /// <summary>
        /// Returns whether a mediator of the supplied type is currently attached.
        /// </summary>
        /// <typeparam name="TMediator">Mediator type to query.</typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsAttached<TMediator>() where TMediator : MediatorBehaviour
        {
            return HasModule("query mediator attachment") && Module.IsMediatorAttached<TMediator>();
        }

        /// <summary>
        /// Returns whether a mediator of the supplied type is currently attached.
        /// </summary>
        /// <param name="mediatorType">Mediator type to query.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsAttached(Type mediatorType)
        {
            return HasModule("query mediator attachment") && Module.IsMediatorAttached(mediatorType);
        }

        // Resolve through either the module facade or the owning mediator context.
        private MvcModule Module => _module != null ? _module : _mediator.ModuleContext;

        // Human-readable name for the caller - used in warning/error messages.
        private string CallerName => _mediator != null ? _mediator.name
                                   : _module   != null ? _module.GetType().Name
                                   : "unknown";

        // Guard every operation because runtime view composition can be requested after teardown.
        private bool HasModule(string operation)
        {
            if (Module != null)
            {
                return true;
            }

            var ownerName = _mediator != null ? _mediator.name : "module";
            MvcDebug.LogError($"Cannot {operation} from '{ownerName}' - owning module reference is missing.");
            return false;
        }

        // Surface style-policy warnings at the call site that performs dynamic attachment.
        private void WarnForCodeAttachment(Type mediatorType)
        {
            var mediatorName = mediatorType != null ? mediatorType.Name : "unknown mediator";
            var module = Module;
            var moduleName = module != null ? module.GetType().Name : "unknown module";
            MvcCompositionStyleWarning.WarnIfDisabled(
                MvcCompositionStyle.Code,
                $"code mediator attachment '{mediatorName}' in module '{moduleName}'");
        }
    }
}
