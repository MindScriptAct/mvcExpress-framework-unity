using mvcExpress.Logging;
using mvcExpress.Plugins;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace mvcExpress
{
    /// <summary>
    /// Central registry and event router for mvcExpress framework plugins.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Plugins register by implementing one or more observer interfaces and calling
    /// <c>Register</c>. The bus detects which interfaces the plugin implements
    /// and routes framework events accordingly.
    /// </para>
    /// <para>
    /// <see cref="ILogObserver"/> routing is production-safe and always compiled.
    /// All other observer routing is editor/dev only and stripped in release builds.
    /// </para>
    /// </remarks>
    public static class MvcPluginBus
    {
        // Production: log observers - always compiled
        private static readonly List<ILogObserver> _logObservers = new List<ILogObserver>(4);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static readonly List<IMessageObserver> _messageObservers = new List<IMessageObserver>(4);
        private static readonly List<IModuleObserver> _moduleObservers = new List<IModuleObserver>(4);
        private static readonly List<IRegistrationObserver> _registrationObservers = new List<IRegistrationObserver>(4);
        private static readonly List<ICommandObserver> _commandObservers = new List<ICommandObserver>(4);

        // Cheap flag mirroring "_messageObservers.Count > 0", checked once per Publish() call so
        // MvcMessageBus can skip the entire per-handler dispatch-tracking block (which otherwise
        // pays for a handler.Target?.GetType() and a ConditionalWeakTable lookup per subscriber,
        // even when FireMessageDispatched would immediately no-op) when nothing is listening.
        internal static bool HasMessageObservers { get; private set; }

        // ConditionalWeakTable maps actor instance to owning module type.
        // Does not prevent GC - no leaks when actors are destroyed.
        private static readonly ConditionalWeakTable<object, StrongBox<Type>> _subscriberModules
            = new ConditionalWeakTable<object, StrongBox<Type>>();
#endif

        // ── Registration ──────────────────────────────────────────────────────

        /// <summary>
        /// Registers a log observer. Works in all build configurations.
        /// </summary>
        public static void Register(ILogObserver observer)
        {
            if (observer == null || _logObservers.Contains(observer)) return;
            _logObservers.Add(observer);
        }

        /// <summary>
        /// Unregisters a log observer.
        /// </summary>
        public static void Unregister(ILogObserver observer)
        {
            _logObservers.Remove(observer);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Registers a plugin for every observer interface it implements.
        /// One call covers plugins that implement multiple interfaces.
        /// </summary>
        public static void Register(object plugin)
        {
            if (plugin == null) return;
            if (plugin is ILogObserver log)          Register(log);
            if (plugin is IMessageObserver msg)      Register(msg);
            if (plugin is IModuleObserver mod)       Register(mod);
            if (plugin is IRegistrationObserver reg) Register(reg);
            if (plugin is ICommandObserver cmd)      Register(cmd);
        }

        /// <summary>
        /// Unregisters a plugin from all observer lists it was registered in.
        /// </summary>
        public static void Unregister(object plugin)
        {
            if (plugin == null) return;
            if (plugin is ILogObserver log)          Unregister(log);
            if (plugin is IMessageObserver msg)      Unregister(msg);
            if (plugin is IModuleObserver mod)       Unregister(mod);
            if (plugin is IRegistrationObserver reg) Unregister(reg);
            if (plugin is ICommandObserver cmd)      Unregister(cmd);
        }

        internal static void Register(IMessageObserver observer)
        {
            if (observer == null || _messageObservers.Contains(observer)) return;
            _messageObservers.Add(observer);
            HasMessageObservers = _messageObservers.Count > 0;
        }

        internal static void Unregister(IMessageObserver observer)
        {
            _messageObservers.Remove(observer);
            HasMessageObservers = _messageObservers.Count > 0;
        }

        internal static void Register(IModuleObserver observer)
        {
            if (observer == null || _moduleObservers.Contains(observer)) return;
            _moduleObservers.Add(observer);
        }

        internal static void Unregister(IModuleObserver observer) => _moduleObservers.Remove(observer);

        internal static void Register(IRegistrationObserver observer)
        {
            if (observer == null || _registrationObservers.Contains(observer)) return;
            _registrationObservers.Add(observer);
        }

        internal static void Unregister(IRegistrationObserver observer) => _registrationObservers.Remove(observer);

        internal static void Register(ICommandObserver observer)
        {
            if (observer == null || _commandObservers.Contains(observer)) return;
            _commandObservers.Add(observer);
        }

        internal static void Unregister(ICommandObserver observer) => _commandObservers.Remove(observer);
#endif

        // ── Log routing (production) ──────────────────────────────────────────
        //
        // These iterate a snapshot (not the live list) so an observer that unregisters itself
        // from inside OnLog/OnWarning/OnError doesn't shrink the list mid-iteration and cause the
        // next observer to be skipped for this dispatch. Exceptions are deliberately NOT caught
        // here: ILogObserver is a documented production extension point for arbitrary third-party
        // code (e.g. forwarding to Sentry), and silently swallowing its exceptions is the same
        // "what happens when user code throws" policy question tracked for the message bus itself
        // (see the messaging semantics decision register) - not a call to make unilaterally here.

        internal static void FireLog(string message, MvcLogContext context)
        {
            if (_logObservers.Count == 0) return;
            var snapshot = _logObservers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
                snapshot[i].OnLog(message, context);
        }

        internal static void FireWarning(string message, MvcLogContext context)
        {
            if (_logObservers.Count == 0) return;
            var snapshot = _logObservers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
                snapshot[i].OnWarning(message, context);
        }

        internal static void FireError(string message, MvcLogContext context)
        {
            if (_logObservers.Count == 0) return;
            var snapshot = _logObservers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
                snapshot[i].OnError(message, context);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD

        // ── Subscriber module tracking ────────────────────────────────────────

        /// <summary>
        /// Called by MessengerApi.Subscribe to associate an actor instance with its owning module.
        /// Only called when at least one IMessageObserver is registered.
        /// </summary>
        internal static void TrackSubscriber(object actorTarget, Type moduleType)
        {
            if (actorTarget == null) return;
            _subscriberModules.AddOrUpdate(actorTarget, new StrongBox<Type>(moduleType));
        }

        /// <summary>
        /// Resolves the owning module type of a subscriber actor from the tracking table.
        /// Returns null for static delegates or untracked actors (global actors).
        /// </summary>
        internal static Type GetSubscriberModuleType(object handlerTarget)
        {
            if (handlerTarget == null) return null;
            return _subscriberModules.TryGetValue(handlerTarget, out var box) ? box.Value : null;
        }

        // ── Message events ────────────────────────────────────────────────────

        // FireMessagePublished/FireMessageDispatched run once per Publish()/per-handler while at
        // least one IMessageObserver is registered - a live dev-tooling path, not a rare one like
        // the lifecycle/registration events below. So instead of ToArray()'s per-call allocation,
        // they dispatch over a reused scratch buffer snapshotted at the start of each call: any
        // registration or unregistration during dispatch mutates only the live _messageObservers
        // list, never the buffer already being iterated, so there is no index bookkeeping to get
        // wrong (an earlier index-adjustment version of this code double-invoked the current
        // observer when it removed a later, not-yet-visited one - see MSG-07 follow-up).
        //
        // The buffer is shared and reused across calls, so a call whose own observer callback
        // triggers a nested Publish (and therefore a reentrant call to one of these two methods)
        // would otherwise have its in-progress snapshot overwritten by the nested call. The depth
        // counter detects that rare case and gives the nested call a one-off array instead.
        private static IMessageObserver[] _messageObserverSnapshot = new IMessageObserver[4];
        private static int _messageObserverDispatchDepth;

        internal static void FireMessagePublished(Type messageType, Type publisherModuleType)
        {
            var count = _messageObservers.Count;
            if (count == 0) return;

            _messageObserverDispatchDepth++;
            try
            {
                var snapshot = SnapshotMessageObservers(count);
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        snapshot[i].OnMessagePublished(messageType, publisherModuleType);
                    }
                    catch (Exception ex)
                    {
                        MvcDebug.LogWarning($"IMessageObserver.OnMessagePublished threw: {ex}");
                    }
                }
            }
            finally
            {
                _messageObserverDispatchDepth--;
            }
        }

        internal static void FireMessageDispatched(
            Type messageType,
            Type publisherModuleType,
            Type subscriberActorType,
            Type subscriberModuleType)
        {
            var count = _messageObservers.Count;
            if (count == 0) return;

            _messageObserverDispatchDepth++;
            try
            {
                var snapshot = SnapshotMessageObservers(count);
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        snapshot[i].OnMessageDispatched(
                            messageType, publisherModuleType, subscriberActorType, subscriberModuleType);
                    }
                    catch (Exception ex)
                    {
                        MvcDebug.LogWarning($"IMessageObserver.OnMessageDispatched threw: {ex}");
                    }
                }
            }
            finally
            {
                _messageObserverDispatchDepth--;
            }
        }

        private static IMessageObserver[] SnapshotMessageObservers(int count)
        {
            if (_messageObserverDispatchDepth > 1)
            {
                // Reentrant: an observer callback triggered another dispatch while the outer call is
                // still mid-loop over the shared buffer. Give the nested call its own array so it
                // can't stomp the outer call's in-progress snapshot. Rare - only when an observer
                // itself causes a Publish.
                return _messageObservers.ToArray();
            }

            if (_messageObserverSnapshot.Length < count) _messageObserverSnapshot = new IMessageObserver[count];
            _messageObservers.CopyTo(_messageObserverSnapshot);
            return _messageObserverSnapshot;
        }

        // ── Module lifecycle events ───────────────────────────────────────────

        internal static void FireModuleRegistered(Type moduleType)
        {
            if (_moduleObservers.Count == 0) return;
            var snapshot = _moduleObservers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                try { snapshot[i].OnModuleRegistered(moduleType); }
                catch (Exception ex) { MvcDebug.LogWarning($"IModuleObserver.OnModuleRegistered threw: {ex}"); }
            }
        }

        internal static void FireModuleInitialized(Type moduleType)
        {
            if (_moduleObservers.Count == 0) return;
            var snapshot = _moduleObservers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                try { snapshot[i].OnModuleInitialized(moduleType); }
                catch (Exception ex) { MvcDebug.LogWarning($"IModuleObserver.OnModuleInitialized threw: {ex}"); }
            }
        }

        internal static void FireModuleUnregistered(Type moduleType)
        {
            if (_moduleObservers.Count == 0) return;
            var snapshot = _moduleObservers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                try { snapshot[i].OnModuleUnregistered(moduleType); }
                catch (Exception ex) { MvcDebug.LogWarning($"IModuleObserver.OnModuleUnregistered threw: {ex}"); }
            }
        }

        // ── Registration events ───────────────────────────────────────────────

        internal static void FireServiceRegistered(Type serviceType, Type moduleType, MvcLogContext.RegistrationSource source)
        {
            if (_registrationObservers.Count == 0) return;
            var snapshot = _registrationObservers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                try { snapshot[i].OnServiceRegistered(serviceType, moduleType, source); }
                catch (Exception ex) { MvcDebug.LogWarning($"IRegistrationObserver.OnServiceRegistered threw: {ex}"); }
            }
        }

        internal static void FireProxyRegistered(Type proxyType, Type moduleType, MvcLogContext.RegistrationSource source)
        {
            if (_registrationObservers.Count == 0) return;
            var snapshot = _registrationObservers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                try { snapshot[i].OnProxyRegistered(proxyType, moduleType, source); }
                catch (Exception ex) { MvcDebug.LogWarning($"IRegistrationObserver.OnProxyRegistered threw: {ex}"); }
            }
        }

        internal static void FireCommandBound(Type commandType, Type messageType, Type moduleType, MvcLogContext.RegistrationSource source)
        {
            if (_registrationObservers.Count == 0) return;
            var snapshot = _registrationObservers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                try { snapshot[i].OnCommandBound(commandType, messageType, moduleType, source); }
                catch (Exception ex) { MvcDebug.LogWarning($"IRegistrationObserver.OnCommandBound threw: {ex}"); }
            }
        }

        internal static void FireMediatorAttached(Type mediatorType, Type moduleType, MvcLogContext.RegistrationSource source)
        {
            if (_registrationObservers.Count == 0) return;
            var snapshot = _registrationObservers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                try { snapshot[i].OnMediatorAttached(mediatorType, moduleType, source); }
                catch (Exception ex) { MvcDebug.LogWarning($"IRegistrationObserver.OnMediatorAttached threw: {ex}"); }
            }
        }

        // ── Command events ────────────────────────────────────────────────────

        internal static void FireCommandExecuted(Type commandType, Type messageType, Type moduleType)
        {
            if (_commandObservers.Count == 0) return;
            var snapshot = _commandObservers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                try { snapshot[i].OnCommandExecuted(commandType, messageType, moduleType); }
                catch (Exception ex) { MvcDebug.LogWarning($"ICommandObserver.OnCommandExecuted threw: {ex}"); }
            }
        }

#endif
    }
}
