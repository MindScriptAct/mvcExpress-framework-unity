using System;
using System.Collections.Generic;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests.Scenarios
{
    /// <summary>
    /// Scoped lifetime redesign scenarios for <see cref="MediatorBehaviour"/> as an injectee, plus the
    /// Logic/View domain-isolation rules that cut across every actor type (Mediator, Proxy). See
    /// InternalSpecs/Architecture/SCOPED_LIFETIME_REDESIGN.md for the full design contract this
    /// file is written against.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public class MediatorAndDomainScopedLifetimeScenarioTests
    {
        // ── Test doubles: leaf, view-scope-only dependency ──────────────────

        private class ViewOnlyLeafDependency
        {
        }

        // ── Test doubles: a scoped dependency exposed via an interface alias, with its own
        // recursive [Inject] field and IMvcLifecycle hooks ──────────────────

        private interface IScopedThing
        {
        }

        private class ScopedThingImpl : IScopedThing, IMvcLifecycle
        {
            [Inject(true)]
            public ViewOnlyLeafDependency Leaf;

            public int OnInitializedCallCount { get; private set; }
            public int OnCleanupCallCount { get; private set; }

            public void OnInitialized() => OnInitializedCallCount++;
            public void OnCleanup() => OnCleanupCallCount++;
        }

        // ── Test doubles: a scoped dependency deliberately exposed to only one domain,
        // shared by the concrete-type dual-domain tests below ────────────────

        private class DualDomainScopedService : IMvcLifecycle
        {
            public int OnInitializedCallCount { get; private set; }
            public int OnCleanupCallCount { get; private set; }

            public void OnInitialized() => OnInitializedCallCount++;
            public void OnCleanup() => OnCleanupCallCount++;
        }

        // ── Test doubles: owning injectees ───────────────────────────────────

        private class ThingInjectingMediator : MediatorBehaviour
        {
            [Inject]
            public IScopedThing Thing;

            // Mirrors a real later call site (OnEnable/Update/message handler) made strictly
            // after CompleteInitialization's BeginViewScope using-block has already exited.
            public bool TryResolveThingNow(out IScopedThing value) => Container.TryResolve(out value);
        }

        private class DualDomainThingInjectingMediator : MediatorBehaviour
        {
            [Inject]
            public DualDomainScopedService Thing;
        }

        private class ThingInjectingProxy : Proxy
        {
            [Inject]
            public IScopedThing Thing;
        }

        private class DualDomainThingInjectingProxy : Proxy
        {
            [Inject]
            public DualDomainScopedService Thing;
        }

        // ── Test doubles: COR-17 disposal-order shapes ───────────────────────
        // Both shapes route disposal-order observations through a shared, test-assigned log so
        // OnCleanup() call order can be asserted without touching internal container state.

        private class NestedLeafDependency : IMvcLifecycle
        {
            public static List<string> DisposalLog;
            public void OnInitialized() { }
            public void OnCleanup() => DisposalLog?.Add("NestedLeaf");
        }

        // Its own [Inject] field resolves NestedLeafDependency as a fresh, nested scoped instance
        // during its own construction - the shape forward CreationOrder already handled correctly.
        private class NestedOuterDependency : IMvcLifecycle
        {
            [Inject]
            public NestedLeafDependency Leaf;
            public void OnInitialized() { }
            public void OnCleanup() => NestedLeafDependency.DisposalLog?.Add("NestedOuter");
        }

        private class NestedScopedFieldMediator : MediatorBehaviour
        {
            [Inject]
            public NestedOuterDependency Outer;
        }

        private class SiblingLeafDependency : IMvcLifecycle
        {
            public static List<string> DisposalLog;
            public void OnInitialized() { }
            public void OnCleanup() => DisposalLog?.Add("SiblingLeaf");
        }

        // Its own [Inject] field resolves SiblingLeafDependency too, but that instance already
        // exists by the time this one constructs (see SiblingScopedFieldsMediator field order) -
        // a cache hit, not a nested creation. This is the COR-17 shape.
        private class SiblingDependentService : IMvcLifecycle
        {
            [Inject]
            public SiblingLeafDependency Leaf;
            public void OnInitialized() { }
            public void OnCleanup() => SiblingLeafDependency.DisposalLog?.Add("SiblingDependent");
        }

        private class SiblingScopedFieldsMediator : MediatorBehaviour
        {
            // Declared (and thus resolved) first, so it's already cached by the time Dependent's
            // own [Inject] field asks for the same type below.
            [Inject]
            public SiblingLeafDependency Leaf;

            [Inject]
            public SiblingDependentService Dependent;
        }

        // Module stub that skips Awake/OnDestroy entirely so the framework singleton (MvcFacade)
        // is never created - matches the pattern in MediatorBehaviourTests.cs.
        private class NoInitModule : MvcModule
        {
            protected override void Awake()
            {
            }

            protected override void OnDestroy()
            {
            }
        }

        // ── Fields ────────────────────────────────────────────────────────────

        private GameObject _moduleGo;
        private NoInitModule _module;
        private MvcDiContainer _container;
        private MvcMessageBus _messageBus;
        private readonly List<GameObject> _trackedGameObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _moduleGo = new GameObject(nameof(NoInitModule));
            _module = _moduleGo.AddComponent<NoInitModule>();
            _container = new MvcDiContainer();
            _messageBus = new MvcMessageBus();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _trackedGameObjects)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _trackedGameObjects.Clear();

            if (_moduleGo != null)
            {
                Object.DestroyImmediate(_moduleGo);
                _moduleGo = null;
            }

            _messageBus?.Dispose();
            _container?.Dispose();
        }

        private T CreateMediator<T>(string name = null) where T : MediatorBehaviour
        {
            var go = new GameObject(name ?? typeof(T).Name);
            _trackedGameObjects.Add(go);
            return go.AddComponent<T>();
        }

        // ── Group A: MediatorBehaviour as a scoped injectee ──────────────────

        [Test]
        public void ScopedDependency_InjectedIntoMediatorViaInterfaceAlias_ConstructsConcreteImplementationType()
        {
            _container.Register(new ScopedThingImpl()).ToViewAs<IScopedThing>().AsScoped();

            var mediator = CreateMediator<ThingInjectingMediator>();
            mediator.Initialize(_module, _container, _messageBus, deferOnInitialized: false);

            Assert.That(mediator.Thing, Is.Not.Null);
            Assert.That(mediator.Thing.GetType(), Is.EqualTo(typeof(ScopedThingImpl)),
                "A scoped dependency requested through an interface alias must still be constructed " +
                "via its concrete implementation type (Activator.CreateInstance(descriptor.ImplementationType)), " +
                "never via the requested interface type.");
        }

        [Test]
        public void ScopedDependency_InjectedIntoMediator_ResolvesOwnInjectFieldUsingViewScope()
        {
            var leaf = new ViewOnlyLeafDependency();
            _container.Register(leaf).ToView().AsPermanent();
            _container.Register(new ScopedThingImpl()).ToViewAs<IScopedThing>().AsScoped();

            var mediator = CreateMediator<ThingInjectingMediator>();
            mediator.Initialize(_module, _container, _messageBus, deferOnInitialized: false);

            var resolved = (ScopedThingImpl)mediator.Thing;
            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved.Leaf, Is.SameAs(leaf),
                "Constructing a scoped dependency must recursively resolve its OWN [Inject] fields - " +
                "here, from the view scope, since the scoped registration itself was exposed via " +
                "ToViewAs - not leave them unset.");
        }

        [Test]
        public void ScopedDependencyImplementingIMvcLifecycle_InjectedIntoMediator_CallsOnInitialized()
        {
            _container.Register(new ScopedThingImpl()).ToViewAs<IScopedThing>().AsScoped();

            var mediator = CreateMediator<ThingInjectingMediator>();
            mediator.Initialize(_module, _container, _messageBus, deferOnInitialized: false);

            var resolved = (ScopedThingImpl)mediator.Thing;
            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved.OnInitializedCallCount, Is.EqualTo(1),
                "A scoped dependency that implements IMvcLifecycle must have OnInitialized() called " +
                "exactly once as part of construction, before the instance is handed to the owning " +
                "Mediator - mirroring how the framework initializes any other lifecycle-aware service.");
        }

        [Test]
        public void ScopedDependency_InjectedIntoMediator_IsReleasedWhenMediatorCleansUp()
        {
            _container.Register(new ScopedThingImpl()).ToViewAs<IScopedThing>().AsScoped();

            var mediator = CreateMediator<ThingInjectingMediator>();
            var go = mediator.gameObject;
            mediator.Initialize(_module, _container, _messageBus, deferOnInitialized: false);

            var resolved = (ScopedThingImpl)mediator.Thing;
            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved.OnCleanupCallCount, Is.EqualTo(0));

            Object.DestroyImmediate(go);
            _trackedGameObjects.Remove(go);

            Assert.That(resolved.OnCleanupCallCount, Is.EqualTo(1),
                "The scoped instance dedicated to this Mediator must be released (OnCleanup called) " +
                "exactly once when the Mediator itself is torn down (CleanupMediator/OnDestroy) - " +
                "per the v2 design's per-injectee ownership rule: the actor dies, its scoped object dies.");
        }

        [Test]
        public void ScopedDependency_InjectedIntoTwoMediatorInstances_EachGetsDedicatedInstance()
        {
            _container.Register(new ScopedThingImpl()).ToViewAs<IScopedThing>().AsScoped();

            var mediatorA = CreateMediator<ThingInjectingMediator>("MediatorA");
            mediatorA.Initialize(_module, _container, _messageBus, deferOnInitialized: false);

            var mediatorB = CreateMediator<ThingInjectingMediator>("MediatorB");
            mediatorB.Initialize(_module, _container, _messageBus, deferOnInitialized: false);

            Assert.That(mediatorA.Thing, Is.Not.Null);
            Assert.That(mediatorB.Thing, Is.Not.Null);
            Assert.That(mediatorB.Thing, Is.Not.SameAs(mediatorA.Thing),
                "Ownership is per-injectee: two different Mediator instances resolving the same scoped " +
                "registration must each get their own dedicated instance, never one shared globally " +
                "across every consumer of the registration.");
        }

        [Test]
        public void ScopedDependency_ResolvedAfterCompleteInitializationWindowCloses_ReturnsSameDedicatedInstance()
        {
            _container.Register(new ScopedThingImpl()).ToViewAs<IScopedThing>().AsScoped();

            var mediator = CreateMediator<ThingInjectingMediator>();
            mediator.Initialize(_module, _container, _messageBus, deferOnInitialized: false);
            // CompleteInitialization's BeginViewScope using-block has already exited by the time
            // Initialize() returns - this call mirrors a later resolve from OnEnable, Update, or a
            // message callback, entirely outside that window.

            bool resolvedLater = mediator.TryResolveThingNow(out var laterValue);

            Assert.That(resolvedLater, Is.True,
                "A later resolve for the same scoped type, made through the actor-type-fixed " +
                "Container.TryResolve (ResolveWithScope(useViewScope: true, ...)), must still succeed - " +
                "a Mediator is a view-layer actor for its whole lifetime, not just during " +
                "CompleteInitialization's BeginViewScope window.");
            Assert.That(laterValue, Is.SameAs(mediator.Thing),
                "The later resolve must return the SAME dedicated instance that was injected during " +
                "initialization, not a second instance - per-injectee ownership is memoized for the " +
                "Mediator's whole lifetime, not just for one ambient resolution window.");
        }

        // ── Group B: Logic/View domain isolation (cross-cutting) ─────────────

        [Test]
        public void LogicOnlyScopedType_InjectedIntoMediator_ThrowsInvalidOperationException()
        {
            _container.Register(new ScopedThingImpl()).ToLogicAs<IScopedThing>().AsScoped();

            var mediator = CreateMediator<ThingInjectingMediator>();

            var ex = Assert.Throws<InvalidOperationException>(
                () => mediator.Initialize(_module, _container, _messageBus, deferOnInitialized: false),
                "A scoped registration exposed only to the Logic domain (.ToLogicAs<T>().AsScoped(), " +
                "never .ToView()/.ToViewAs<T>()) must not be resolvable from a Mediator, which is a " +
                "View-domain injectee. Resolving it must fail clearly, not silently hand back a " +
                "cross-domain instance.");

            Assert.That(ex.Message, Does.Not.Contain("no scoped resolution scope is active"),
                "The failure must come from a real Logic/View domain check, not from today's incidental " +
                "ambient-scope-missing error - that error fires for every scoped injection into a Mediator " +
                "regardless of domain, so it would mask a genuine cross-domain leak once Scoped v2 removes " +
                "the legacy ambient-scope requirement entirely.");
        }

        [Test]
        public void ViewOnlyScopedType_InjectedIntoProxy_ThrowsInvalidOperationException()
        {
            _container.Register(new ScopedThingImpl()).ToViewAs<IScopedThing>().AsScoped();

            var proxy = new ThingInjectingProxy();

            var ex = Assert.Throws<InvalidOperationException>(
                () => proxy.Initialize(typeof(ThingInjectingProxy), _messageBus, _container),
                "A scoped registration exposed only to the View domain (.ToViewAs<T>().AsScoped(), never " +
                ".ToLogic()/.ToLogicAs<T>()) must not be resolvable from a Proxy, which is a Logic-domain " +
                "injectee. Resolving it must fail clearly, not silently hand back a cross-domain instance.");

            Assert.That(ex.Message, Does.Not.Contain("no scoped resolution scope is active"),
                "The failure must come from a real Logic/View domain check, not from today's incidental " +
                "ambient-scope-missing error - that error fires for every scoped injection into a Proxy " +
                "regardless of domain, so it would mask a genuine cross-domain leak once Scoped v2 removes " +
                "the legacy ambient-scope requirement entirely.");
        }

        [Test]
        public void DualDomainScopedRegistration_ExposedToBothDomainsInSingleChain_RegistersSuccessfully()
        {
            Assert.DoesNotThrow(
                () => _container.Register(new DualDomainScopedService()).ToLogic().ToView().AsScoped(),
                "A single .AsScoped() builder chain that exposes the SAME registration to both the Logic " +
                "and View domains (.ToLogic().ToView(), one chain, one registrationId) must be allowed to " +
                "register - the redesign explicitly supports one dual-domain registration sharing one " +
                "instance per injectee. Today this throws because the legacy MarkScoped() single, non-" +
                "domain-partitioned Type registry (still invoked alongside RegisterScopedIdentity for bookkeeping) " +
                "rejects registering the same concrete Type as scoped twice - once from the logic-types " +
                "loop, once from the view-types loop - even though both come from one chain.");

            var descriptor = _container.GetScopedRegistration(typeof(DualDomainScopedService));
            Assert.That(descriptor, Is.Not.Null);
            Assert.That(descriptor.ExposedToLogic, Is.True);
            Assert.That(descriptor.ExposedToView, Is.True);
        }

        [Test]
        public void DualDomainScopedType_ResolvesSuccessfullyFromMediatorInjectee()
        {
            _container.Register(new DualDomainScopedService()).ToLogic().ToView().AsScoped();

            var mediator = CreateMediator<DualDomainThingInjectingMediator>();

            Assert.DoesNotThrow(
                () => mediator.Initialize(_module, _container, _messageBus, deferOnInitialized: false),
                "A scoped registration explicitly exposed to BOTH domains in one chain must resolve " +
                "successfully from a View-domain injectee (Mediator).");
            Assert.That(mediator.Thing, Is.Not.Null);
        }

        [Test]
        public void DualDomainScopedType_ResolvesSuccessfullyFromProxyInjectee()
        {
            _container.Register(new DualDomainScopedService()).ToLogic().ToView().AsScoped();

            var proxy = new DualDomainThingInjectingProxy();

            Assert.DoesNotThrow(
                () => proxy.Initialize(typeof(DualDomainThingInjectingProxy), _messageBus, _container),
                "A scoped registration explicitly exposed to BOTH domains in one chain must resolve " +
                "successfully from a Logic-domain injectee (Proxy) too.");
            Assert.That(proxy.Thing, Is.Not.Null);
        }

        [Test]
        public void DualDomainScopedType_MediatorAndProxyInjectees_GetDistinctDedicatedInstances()
        {
            // Domain-exposure and per-injectee ownership are two INDEPENDENT axes of the v2 design:
            // ScopedRegistrationDescriptor.ExposedToLogic/ExposedToView decide WHICH domains may
            // resolve a registration at all; the ConditionalWeakTable<object, ScopedInstanceScope>
            // keyed by the injectee itself decides WHO owns the resulting instance. A registration
            // being dual-domain does NOT mean the constructed instance is shared globally across
            // every consumer - a Mediator and a Proxy are two DIFFERENT injectees, so even though
            // they resolve through the same registrationId, each must get its own dedicated
            // instance, constructed/initialized/released independently of the other's.
            _container.Register(new DualDomainScopedService()).ToLogic().ToView().AsScoped();

            var mediator = CreateMediator<DualDomainThingInjectingMediator>();
            mediator.Initialize(_module, _container, _messageBus, deferOnInitialized: false);

            var proxy = new DualDomainThingInjectingProxy();
            proxy.Initialize(typeof(DualDomainThingInjectingProxy), _messageBus, _container);

            Assert.That(mediator.Thing, Is.Not.Null);
            Assert.That(proxy.Thing, Is.Not.Null);
            Assert.That(proxy.Thing, Is.Not.SameAs(mediator.Thing),
                "A Mediator and a Proxy are different injectees; resolving the same dual-domain scoped " +
                "registration into each must produce two distinct, independently-owned instances - one " +
                "instance per (injectee, registrationId), not one instance per registrationId globally.");
        }

        [Test]
        public void AliasedScopedRegistration_ConcreteTypeAndInterfaceAlias_ShareSameRegistrationId()
        {
            _container.Register(new ScopedThingImpl()).ToLogic().ToLogicAs<IScopedThing>().AsScoped();

            var concreteDescriptor = _container.GetScopedRegistration(typeof(ScopedThingImpl));
            var interfaceDescriptor = _container.GetScopedRegistration(typeof(IScopedThing));

            Assert.That(concreteDescriptor, Is.Not.Null);
            Assert.That(interfaceDescriptor, Is.Not.Null);
            Assert.That(interfaceDescriptor.RegistrationId, Is.EqualTo(concreteDescriptor.RegistrationId),
                "Identity for a scoped registration is the registrationId assigned once per .AsScoped() " +
                "builder chain, not the requested Type - the concrete type and an interface alias " +
                "registered in the SAME chain must map to the same registrationId, so aliasing resolves " +
                "to one instance per injectee instead of one instance per alias.");
        }

        // ── Group C: COR-17 - teardown disposal order must respect dependency, not raw creation order ──

        [Test]
        public void ScopedNestedDependency_OuterOwnsInnerViaOwnInjectField_DisposesOuterBeforeInner()
        {
            NestedLeafDependency.DisposalLog = new List<string>();
            _container.Register(new NestedLeafDependency()).ToView().AsScoped();
            _container.Register(new NestedOuterDependency()).ToView().AsScoped();

            var mediator = CreateMediator<NestedScopedFieldMediator>();
            var go = mediator.gameObject;
            mediator.Initialize(_module, _container, _messageBus, deferOnInitialized: false);

            Assert.That(mediator.Outer, Is.Not.Null);
            Assert.That(mediator.Outer.Leaf, Is.Not.Null);

            Object.DestroyImmediate(go);
            _trackedGameObjects.Remove(go);

            Assert.That(NestedLeafDependency.DisposalLog, Is.EqualTo(new[] { "NestedOuter", "NestedLeaf" }),
                "The nested shape (an outer scoped instance's OWN [Inject] field triggers a further " +
                "scoped resolution during its own construction) already disposed correctly before " +
                "COR-17's fix - the outer dependent must keep being disposed before the inner " +
                "dependency it owns, this must not regress.");
        }

        [Test]
        public void ScopedSiblingFields_SecondFieldDependsOnFirst_DisposesDependentBeforeSharedDependency()
        {
            SiblingLeafDependency.DisposalLog = new List<string>();
            _container.Register(new SiblingLeafDependency()).ToView().AsScoped();
            _container.Register(new SiblingDependentService()).ToView().AsScoped();

            var mediator = CreateMediator<SiblingScopedFieldsMediator>();
            var go = mediator.gameObject;
            mediator.Initialize(_module, _container, _messageBus, deferOnInitialized: false);

            Assert.That(mediator.Leaf, Is.Not.Null);
            Assert.That(mediator.Dependent, Is.Not.Null);
            Assert.That(mediator.Dependent.Leaf, Is.SameAs(mediator.Leaf),
                "Both top-level fields must resolve to the SAME scoped Leaf instance - that's exactly " +
                "the shared-dependency shape COR-17 is about (two independent top-level [Inject] " +
                "fields on one owner, one of them discovering the other's dependency via its own " +
                "[Inject] field instead of creating it).");

            Object.DestroyImmediate(go);
            _trackedGameObjects.Remove(go);

            Assert.That(SiblingLeafDependency.DisposalLog, Is.EqualTo(new[] { "SiblingDependent", "SiblingLeaf" }),
                "Dependent (whose own [Inject] field references Leaf, resolved as a cache hit since " +
                "Leaf was declared/resolved first) must be disposed BEFORE Leaf. Raw CreationOrder " +
                "lists Leaf before Dependent - forward-order disposal would tear down Leaf first while " +
                "Dependent still holds a live reference to it, which is exactly the COR-17 bug.");
        }
    }
}
