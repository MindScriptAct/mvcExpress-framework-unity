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
        }

        internal static void Unregister(IMessageObserver observer) => _messageObservers.Remove(observer);

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

        internal static void FireLog(string message, MvcLogContext context)
        {
            for (int i = 0; i < _logObservers.Count; i++)
                _logObservers[i].OnLog(message, context);
        }

        internal static void FireWarning(string message, MvcLogContext context)
        {
            for (int i = 0; i < _logObservers.Count; i++)
                _logObservers[i].OnWarning(message, context);
        }

        internal static void FireError(string message, MvcLogContext context)
        {
            for (int i = 0; i < _logObservers.Count; i++)
                _logObservers[i].OnError(message, context);
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

        internal static void FireMessagePublished(Type messageType, Type publisherModuleType)
        {
            if (_messageObservers.Count == 0) return;
            for (int i = 0; i < _messageObservers.Count; i++)
                _messageObservers[i].OnMessagePublished(messageType, publisherModuleType);
        }

        internal static void FireMessageDispatched(
            Type messageType,
            Type publisherModuleType,
            Type subscriberActorType,
            Type subscriberModuleType)
        {
            if (_messageObservers.Count == 0) return;
            for (int i = 0; i < _messageObservers.Count; i++)
                _messageObservers[i].OnMessageDispatched(
                    messageType, publisherModuleType, subscriberActorType, subscriberModuleType);
        }

        // ── Module lifecycle events ───────────────────────────────────────────

        internal static void FireModuleRegistered(Type moduleType)
        {
            if (_moduleObservers.Count == 0) return;
            for (int i = 0; i < _moduleObservers.Count; i++)
                _moduleObservers[i].OnModuleRegistered(moduleType);
        }

        internal static void FireModuleInitialized(Type moduleType)
        {
            if (_moduleObservers.Count == 0) return;
            for (int i = 0; i < _moduleObservers.Count; i++)
                _moduleObservers[i].OnModuleInitialized(moduleType);
        }

        internal static void FireModuleUnregistered(Type moduleType)
        {
            if (_moduleObservers.Count == 0) return;
            for (int i = 0; i < _moduleObservers.Count; i++)
                _moduleObservers[i].OnModuleUnregistered(moduleType);
        }

        // ── Registration events ───────────────────────────────────────────────

        internal static void FireServiceRegistered(Type serviceType, Type moduleType, MvcLogContext.RegistrationSource source)
        {
            if (_registrationObservers.Count == 0) return;
            for (int i = 0; i < _registrationObservers.Count; i++)
                _registrationObservers[i].OnServiceRegistered(serviceType, moduleType, source);
        }

        internal static void FireProxyRegistered(Type proxyType, Type moduleType, MvcLogContext.RegistrationSource source)
        {
            if (_registrationObservers.Count == 0) return;
            for (int i = 0; i < _registrationObservers.Count; i++)
                _registrationObservers[i].OnProxyRegistered(proxyType, moduleType, source);
        }

        internal static void FireCommandBound(Type commandType, Type messageType, Type moduleType, MvcLogContext.RegistrationSource source)
        {
            if (_registrationObservers.Count == 0) return;
            for (int i = 0; i < _registrationObservers.Count; i++)
                _registrationObservers[i].OnCommandBound(commandType, messageType, moduleType, source);
        }

        internal static void FireMediatorAttached(Type mediatorType, Type moduleType, MvcLogContext.RegistrationSource source)
        {
            if (_registrationObservers.Count == 0) return;
            for (int i = 0; i < _registrationObservers.Count; i++)
                _registrationObservers[i].OnMediatorAttached(mediatorType, moduleType, source);
        }

        // ── Command events ────────────────────────────────────────────────────

        internal static void FireCommandExecuted(Type commandType, Type messageType, Type moduleType)
        {
            if (_commandObservers.Count == 0) return;
            for (int i = 0; i < _commandObservers.Count; i++)
                _commandObservers[i].OnCommandExecuted(commandType, messageType, moduleType);
        }

#endif
    }
}
