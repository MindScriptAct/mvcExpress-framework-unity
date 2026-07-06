using mvcExpress.Internal.Interfaces;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace mvcExpress
{
    public abstract partial class MvcModule
    {
        #region -------------------- Mediator host internals --------------------

        internal bool AttachMediator(MediatorBehaviour mediator)
        {
            if (mediator == null)
            {
                // Setup error: null mediator reference.
                var stackTrace = new System.Diagnostics.StackTrace(true);
                var callerFrame = stackTrace.GetFrame(1);
                var filePath = callerFrame?.GetFileName();
                var lineNumber = callerFrame?.GetFileLineNumber() ?? 0;
                var fileName = !string.IsNullOrEmpty(filePath) ? System.IO.Path.GetFileName(filePath) : "Unknown";
                
                var errorMessage = $"Critical error in module {GetType().Name}:\n\n" +
                    $"Cannot attach NULL mediator reference!\n\n" +
                    $"Called from: {fileName}:{lineNumber}\n\n" +
                    $"SOLUTION:\n" +
                    $"1. Check your module's Inspector and assign the mediator reference\n" +
                    $"2. Or remove the AttachMediator() call if the mediator is not needed\n" +
                    $"3. Or use AttachPrefabMediator<T>() to instantiate from a prefab instead\n\n" +
                    $"This is a setup error that must be fixed before the module can run.";
                
                mvcExpress.Logging.MvcDebug.LogError(errorMessage);
                
                throw new System.ArgumentNullException(
                    nameof(mediator),
                    $"Cannot attach null mediator in module {GetType().Name}. " +
                    $"Check module Inspector or remove AttachMediator() call. " +
                    $"Called from {fileName}:{lineNumber}");
            }

            if (_initializer == null)
            {
                mvcExpress.Logging.MvcDebug.LogError($"Cannot attach mediator '{mediator.name}' - module {GetType().Name} is not initialized yet.");
                return false;
            }

            // If the module isn't fully initialized yet, treat this as a setup-time registration.
            // The mediator will be initialized/completed as part of the normal initialization pipeline.
            if (!_initializer.IsInitialized)
            {
                if (_initializer.MediatorRegistrar != null && _initializer.MediatorRegistrar.IsManualRegistrationLocked)
                {
                    mvcExpress.Logging.MvcDebug.LogWarning($"Cannot attach mediator '{mediator.name}' during initialization because manual mediator registration is locked in module {GetType().Name}.");
                    return false;
                }

                // Make IsMediatorAttached(...) reflect the attachment immediately during AttachMediators().
                _initializer.MediatorRegistrar?.AddDeferredRegisteredMediator(mediator);

                // Keep manual list for backward compatibility / existing pipeline semantics.
                if (!_manualMediators.Contains(mediator))
                    _manualMediators.Add(mediator);

#if UNITY_EDITOR || MVC_LOGGING
                // LOG: Mediator attached by code (reference or prefab)
                // Detect if this is called from AttachPrefabMediator
                var caller = new System.Diagnostics.StackTrace(true).GetFrame(1);
                var callerMethod = caller?.GetMethod();
                bool isPrefab = callerMethod != null && callerMethod.Name == "AttachPrefabMediator";
                
                mvcExpress.Logging.MvcLogInternal.LogMediatorAttached(
                    mediator.GetType().Name,
                    mediator.gameObject.name,
                    this,
                    mvcExpress.Logging.MvcLogContext.RegistrationSource.Code,
                    mediator.gameObject,
                    caller?.GetFileName(),
                    caller?.GetFileLineNumber() ?? 0,
                    isPrefab);
#endif

                return true;
            }

            if (_diContainer == null || _messageBus == null)
            {
                mvcExpress.Logging.MvcDebug.LogError($"Cannot attach mediator '{mediator.name}' - module {GetType().Name} core services are missing.");
                return false;
            }

            // Runtime attach (module already initialized)
            return _initializer.MediatorRegistrar.AddRuntimeMediator(mediator);
        }

        /// <summary>
        /// Attach a mediator prefab using the default ModuleViewContainer.
        /// </summary>
        /// <typeparam name="TMediator">Type of mediator to attach.</typeparam>
        /// <returns>True if successful, false otherwise.</returns>
        internal bool AttachPrefabMediator<TMediator>() where TMediator : MediatorBehaviour
        {
            return AttachPrefabMediator<TMediator>(ModuleViewContainer);
        }

        internal bool AttachPrefabMediator<TMediator>(Transform parent) where TMediator : MediatorBehaviour
        {
            return AttachPrefabMediator(typeof(TMediator), parent);
        }

        internal bool AttachPrefabMediator<TMediator>(Scene scene) where TMediator : MediatorBehaviour
        {
            return AttachPrefabMediator(typeof(TMediator), scene);
        }

        internal bool AttachPrefabMediator(Type mediatorType, Scene scene)
        {
            if (mediatorType == null)
            {
                mvcExpress.Logging.MvcDebug.LogWarning($"Cannot attach mediator - mediatorType is null in module {GetType().Name}.");
                return false;
            }

            if (!typeof(MediatorBehaviour).IsAssignableFrom(mediatorType))
            {
                mvcExpress.Logging.MvcDebug.LogWarning($"Cannot attach mediator type '{mediatorType.FullName}' because it does not derive from MediatorBehaviour in module {GetType().Name}.");
                return false;
            }

            if (_initializer == null)
            {
                mvcExpress.Logging.MvcDebug.LogError($"Cannot attach mediator type '{mediatorType.FullName}' - module {GetType().Name} is not initialized yet.");
                return false;
            }

            EnsureMvcContainers();
            ImportMediatorPrefabMappings();

            if (!TryGetMediatorPrefab(mediatorType, out var prefab) || prefab == null)
            {
                var viewName = _viewContainer != null ? _viewContainer.name : "<null>";
                var mappingCount = _viewContainer != null && _viewContainer.MediatorPrefabs != null ? _viewContainer.MediatorPrefabs.Length : 0;

                var errorMessage = $"Critical error in module {GetType().Name}:\n\n" +
                    $"Cannot attach mediator type '{mediatorType.FullName}' - no prefab mapping found!\n\n" +
                    $"ViewContainer: '{viewName}'\n" +
                    $"Current Mappings: {mappingCount}\n\n" +
                    $"SOLUTION:\n" +
                    $"1. Select the module's '{viewName}' GameObject in the hierarchy\n" +
                    $"2. In Inspector, find 'Mediator Prefabs' section\n" +
                    $"3. Click '+' and assign the prefab for '{mediatorType.Name}'\n" +
                    $"4. The prefab must have '{mediatorType.Name}' component on its root\n\n" +
                    $"This is a setup error that must be fixed before the mediator can be attached.";

                mvcExpress.Logging.MvcDebug.LogError(errorMessage);

                throw new System.InvalidOperationException(
                    $"Cannot attach mediator type '{mediatorType.FullName}' - no prefab mapping found in module {GetType().Name}. " +
                    $"Add it to the module's View container 'Mediator Prefabs' list.");
            }

            var instanceGo = UnityEngine.Object.Instantiate(prefab);
            instanceGo.name = $"{prefab.name} (Mediator)";
            SceneManager.MoveGameObjectToScene(instanceGo, scene);

            var mediator = instanceGo.GetComponent(mediatorType) as MediatorBehaviour;
            if (mediator == null)
            {
                mvcExpress.Logging.MvcDebug.LogError($"Prefab '{prefab.name}' does not contain mediator component '{mediatorType.FullName}' in module {GetType().Name}.");
                UnityEngine.Object.Destroy(instanceGo);
                return false;
            }

            return AttachMediator(mediator);
        }

        internal bool AttachPrefabMediator(Type mediatorType, Transform parent)
        {
            if (mediatorType == null)
            {
                mvcExpress.Logging.MvcDebug.LogWarning($"Cannot attach mediator - mediatorType is null in module {GetType().Name}.");
                return false;
            }

            if (parent == null)
            {
                mvcExpress.Logging.MvcDebug.LogError($"Cannot attach mediator type '{mediatorType.FullName}' - parent is null in module {GetType().Name}.");
                return false;
            }

            if (!typeof(MediatorBehaviour).IsAssignableFrom(mediatorType))
            {
                mvcExpress.Logging.MvcDebug.LogWarning($"Cannot attach mediator type '{mediatorType.FullName}' because it does not derive from MediatorBehaviour in module {GetType().Name}.");
                return false;
            }

            if (_initializer == null)
            {
                mvcExpress.Logging.MvcDebug.LogError($"Cannot attach mediator type '{mediatorType.FullName}' - module {GetType().Name} is not initialized yet.");
                return false;
            }

            // Prefab instantiation is supported both during init and at runtime.
            // The created mediator will be queued (deferred init) if the module isn't fully initialized yet.

            EnsureMvcContainers();
            ImportMediatorPrefabMappings();

            if (!TryGetMediatorPrefab(mediatorType, out var prefab) || prefab == null)
            {
                var viewName = _viewContainer != null ? _viewContainer.name : "<null>";
                var mappingCount = _viewContainer != null && _viewContainer.MediatorPrefabs != null ? _viewContainer.MediatorPrefabs.Length : 0;

                var errorMessage = $"Critical error in module {GetType().Name}:\n\n" +
                    $"Cannot attach mediator type '{mediatorType.FullName}' - no prefab mapping found!\n\n" +
                    $"ViewContainer: '{viewName}'\n" +
                    $"Current Mappings: {mappingCount}\n\n" +
                    $"SOLUTION:\n" +
                    $"1. Select the module's '{viewName}' GameObject in the hierarchy\n" +
                    $"2. In Inspector, find 'Mediator Prefabs' section\n" +
                    $"3. Click '+' and assign the prefab for '{mediatorType.Name}'\n" +
                    $"4. The prefab must have '{mediatorType.Name}' component on its root\n\n" +
                    $"This is a setup error that must be fixed before the mediator can be attached.";

                mvcExpress.Logging.MvcDebug.LogError(errorMessage);

                throw new System.InvalidOperationException(
                    $"Cannot attach mediator type '{mediatorType.FullName}' - no prefab mapping found in module {GetType().Name}. " +
                    $"Add it to the module's View container 'Mediator Prefabs' list.");
            }

            var instanceGo = UnityEngine.Object.Instantiate(prefab);
            instanceGo.name = $"{prefab.name} (Mediator)";
            instanceGo.transform.SetParent(parent, false);

            var mediator = instanceGo.GetComponent(mediatorType) as MediatorBehaviour;
            if (mediator == null)
            {
                mvcExpress.Logging.MvcDebug.LogError($"Prefab '{prefab.name}' does not contain mediator component '{mediatorType.FullName}' in module {GetType().Name}.");
                UnityEngine.Object.Destroy(instanceGo);
                return false;
            }

            // AttachMediator will handle logging
            return AttachMediator(mediator);
        }

        internal bool DetachMediator(MediatorBehaviour mediator)
        {
            if (mediator == null)
            {
                mvcExpress.Logging.MvcDebug.LogWarning($"Cannot detach null mediator from module {GetType().Name}.");
                return false;
            }

            if (_initializer == null)
            {
                mvcExpress.Logging.MvcDebug.LogWarning($"Cannot detach mediator '{mediator.name}' - module {GetType().Name} is not initialized.");
                return false;
            }

            return _initializer.MediatorRegistrar.RemoveRuntimeMediator(mediator);
        }

        internal bool DetachMediator<TMediator>() where TMediator : MediatorBehaviour
        {
            return DetachMediator(typeof(TMediator));
        }

        internal bool DetachMediator(Type mediatorType)
        {
            if (mediatorType == null)
            {
                mvcExpress.Logging.MvcDebug.LogWarning($"Cannot detach mediator - mediatorType is null in module {GetType().Name}.");
                return false;
            }

            if (_initializer == null)
            {
                mvcExpress.Logging.MvcDebug.LogWarning($"Cannot detach mediator type '{mediatorType.FullName}' - module {GetType().Name} is not initialized.");
                return false;
            }

            var registrar = _initializer.MediatorRegistrar;
            var runtime = registrar.RuntimeMediators;

            for (int i = 0; i < runtime.Count; i++)
            {
                var m = runtime[i];
                if (m == null) continue;

                if (mediatorType.IsAssignableFrom(m.GetType()))
                {
                    return registrar.RemoveRuntimeMediator(m);
                }
            }

            mvcExpress.Logging.MvcDebug.LogWarning($"No runtime mediator of type '{mediatorType.FullName}' found to detach in module {GetType().Name}.");
            return false;
        }

        internal bool DetachAllMediators()
        {
            return DetachAllRuntimeMediatorsInternal();
        }

        internal bool IsMediatorAttached<TMediator>() where TMediator : MediatorBehaviour
        {
            return IsMediatorAttached(typeof(TMediator));
        }

        internal bool IsMediatorAttached(Type mediatorType)
        {
            if (mediatorType == null)
            {
                mvcExpress.Logging.MvcDebug.LogWarning($"Cannot query mediator attachment - mediatorType is null in module {GetType().Name}.");
                return false;
            }

            if (_initializer == null)
                return false;

            var registrar = _initializer.MediatorRegistrar;

            // During initialization, serialized mediators are registered before AttachMediators() runs.
            // Those are tracked in RegisteredMediators (and will later be fully initialized).
            var registered = registrar.RegisteredMediators;
            for (int i = 0; i < registered.Count; i++)
            {
                var m = registered[i];
                if (m == null) continue;
                if (mediatorType.IsAssignableFrom(m.GetType()))
                    return true;
            }

            // Runtime-attached mediators (added via AttachMediator / AttachPrefabMediator)
            var runtime = registrar.RuntimeMediators;
            for (int i = 0; i < runtime.Count; i++)
            {
                var m = runtime[i];
                if (m == null) continue;
                if (mediatorType.IsAssignableFrom(m.GetType()))
                    return true;
            }

            return false;
        }

        #endregion Mediator host internals
    }
}
