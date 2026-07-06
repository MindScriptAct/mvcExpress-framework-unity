using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Interfaces;
using mvcExpress.Internal.Messaging;
using mvcExpress.Logging;
using System;

namespace mvcExpress
{
    /// <summary>
    /// Lightweight context passed into actor-facing APIs so they can resolve containers, modules, and logging category.
    /// </summary>
    internal readonly struct MvcActorContext
    {
        // Actor APIs are structs for low allocation; this context keeps their shared state compact.
        internal readonly object Actor;
        internal readonly MvcModule Module;
        internal readonly Type ModuleType;
        internal readonly MvcDiContainer DiContainer;
        // Concrete type, not IMessagePublisher: MvcMessageBus is sealed and the only
        // implementation, and every actor Publish call goes through this field. Storing the
        // interface would force interface dispatch (and defeat AggressiveInlining/IL2CPP
        // generic sharing) on that hot path for no actual polymorphic benefit.
        internal readonly MvcMessageBus MessagePublisher;
        internal readonly MvcLogContext.LogCategory Category;

        internal MvcActorContext(
            object actor,
            MvcModule module,
            Type moduleType,
            MvcDiContainer diContainer,
            IMessagePublisher messagePublisher,
            MvcLogContext.LogCategory category)
        {
            Actor = actor;
            Module = module;
            ModuleType = module != null ? module.ModuleType : moduleType;
            DiContainer = diContainer;
            // Constructor keeps accepting the interface so every call site across Proxy/
            // ProxyBehaviour/CommandBase/MvcModule/MediatorBehaviour stays unchanged; the
            // concrete type is recovered once here.
            MessagePublisher = (MvcMessageBus)messagePublisher;
            Category = category;
        }

        internal MvcModule ResolveModule()
        {
            // Global actors may only know the module type, so resolve the live module lazily.
            if (Module != null)
            {
                return Module;
            }

            if (ModuleType == null || MvcFacade.InstanceOrNull == null)
            {
                return null;
            }

            return MvcFacade.InstanceOrNull.TryGetModule(ModuleType, out var module) ? module : null;
        }
    }
}
