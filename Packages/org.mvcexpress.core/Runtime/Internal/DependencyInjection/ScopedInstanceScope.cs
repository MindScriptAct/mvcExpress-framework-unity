using System;
using System.Collections.Generic;

namespace mvcExpress.Internal.DependencyInjection
{
    /// <summary>
    /// Identity for one <c>.AsScoped()</c> registration. Assigned once per builder chain, not
    /// per exposed Type, so an aliased registration (concrete + interface, or Logic + View)
    /// resolves to one instance per injectee instead of one instance per alias.
    /// </summary>
    internal sealed class ScopedRegistrationDescriptor
    {
        public readonly int RegistrationId;
        public readonly Type ImplementationType;
        public readonly bool ExposedToLogic;
        public readonly bool ExposedToView;

        public ScopedRegistrationDescriptor(int registrationId, Type implementationType, bool exposedToLogic, bool exposedToView)
        {
            RegistrationId = registrationId;
            ImplementationType = implementationType ?? throw new ArgumentNullException(nameof(implementationType));
            ExposedToLogic = exposedToLogic;
            ExposedToView = exposedToView;
        }
    }

    /// <summary>
    /// Owned by exactly one injectee (Proxy/ProxyBehaviour/MediatorBehaviour/MvcCommandBase/
    /// plain service). Created lazily on that injectee's first scoped resolution; disposed
    /// exactly once when the injectee itself is torn down. Keyed by
    /// <see cref="ScopedRegistrationDescriptor.RegistrationId"/>, not by Type.
    /// </summary>
    internal sealed class ScopedInstanceScope
    {
        public readonly object SyncRoot = new object();
        public readonly Dictionary<int, object> Instances = new Dictionary<int, object>();
        public readonly List<object> CreationOrder = new List<object>();

        // Instances currently mid-construction (own [Inject] fields still being resolved), most
        // recently started last. Lets a resolution triggered from inside another instance's own
        // injection - whether it creates a fresh instance or hits the cache of an already-built
        // sibling - identify who is asking, so DependencyEdges can record it. See COR-17.
        public readonly List<object> ConstructionStack = new List<object>();

        // dependent -> the instances it resolved (directly) while it was mid-construction, whether
        // freshly created or an already-cached sibling. Teardown disposes dependents strictly
        // before the dependencies recorded here, instead of relying on raw CreationOrder - which
        // only happens to match dependency order for the nested-construction shape, not for two
        // independent top-level fields where the second one's own [Inject] field turns out to
        // share the first's dependency. See COR-17.
        public readonly Dictionary<object, List<object>> DependencyEdges =
            new Dictionary<object, List<object>>(MvcDiContainer.ReferenceIdentityComparer.Instance);

        public bool IsDisposed { get; private set; }

        public void MarkDisposed()
        {
            IsDisposed = true;
        }
    }
}
