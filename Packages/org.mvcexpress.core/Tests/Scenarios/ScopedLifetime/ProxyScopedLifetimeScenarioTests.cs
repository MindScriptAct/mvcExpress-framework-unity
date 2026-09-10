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
    /// Scoped lifetime redesign scenarios for <see cref="Proxy"/> and <see cref="ProxyBehaviour"/> as
    /// injectees - see InternalSpecs/Architecture/SCOPED_LIFETIME_REDESIGN.md for the full design
    /// contract this file is written against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Known, accepted limitation (not covered by any test here, per the design doc): if a
    /// <see cref="Proxy"/> is only ever torn down via its finalizer (<c>disposing == false</c>),
    /// <see cref="Proxy.OnCleanup"/> never runs and its scoped dependencies are never
    /// deterministically released - the <c>ConditionalWeakTable</c> backing store prevents a hard
    /// leak but does not run <c>Dispose()</c>/<c>OnCleanup()</c> in that case. This mirrors
    /// <see cref="Proxy"/>'s own pre-existing finalizer behavior and is out of scope by design
    /// (GC timing in Unity tests is unreliable). Only the explicit-Dispose path is tested below.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("Scenario")]
    public class ProxyScopedLifetimeScenarioTests
    {
        // ── Test doubles: leaf scoped dependency ─────────────────────────────

        private interface IScopedLeaf
        {
            int Id { get; }
        }

        private class ScopedLeaf : IScopedLeaf, IMvcLifecycle, IDisposable
        {
            private static int _nextId;
            public static void ResetIds() => _nextId = 0;

            public ScopedLeaf() => Id = ++_nextId;

            public int Id { get; }
            public int OnInitializedCount { get; private set; }
            public int OnCleanupCount { get; private set; }
            public bool IsDisposed { get; private set; }

            public void OnInitialized() => OnInitializedCount++;
            public void OnCleanup() => OnCleanupCount++;
            public void Dispose() => IsDisposed = true;
        }

        // ── Test doubles: a scoped dependency with its own recursive [Inject] ──

        private class PermanentHelper
        {
        }

        private class ScopedWrapperWithPermanentDep : IMvcLifecycle
        {
            [Inject] public PermanentHelper Helper;

            public void OnInitialized()
            {
            }

            public void OnCleanup()
            {
            }
        }

        // ── Test doubles: a scoped dependency nesting ANOTHER scoped dependency ──

        private class ScopedWrapperWithNestedScoped : IMvcLifecycle
        {
            [Inject] public ScopedLeaf NestedLeaf;

            public void OnInitialized()
            {
            }

            public void OnCleanup()
            {
            }
        }

        // ── Test doubles: a scoped dependency that is itself a code-only Proxy ──

        private class ScopedProxyDependency : Proxy
        {
            [Inject] public ScopedLeaf NestedLeaf;
            public int OnInitializedCount { get; private set; }
            public int OnCleanupCount { get; private set; }

            protected override void OnInitialized() => OnInitializedCount++;
            protected override void OnCleanup() => OnCleanupCount++;
        }

        // ── Test doubles: owning injectees ──────────────────────────────────

        private class LeafConsumerProxy : Proxy
        {
            [Inject] public ScopedLeaf Leaf;
        }

        private class LeafConsumerProxyBehaviour : ProxyBehaviour
        {
            [Inject] public ScopedLeaf Leaf;
        }

        // Throws from OnCleanup() - used to prove scoped-dependency release survives a throwing
        // proxy lifecycle callback instead of being skipped.
        private class ThrowingOnCleanupConsumerProxy : Proxy
        {
            [Inject] public ScopedLeaf Leaf;
            protected override void OnCleanup() => throw new InvalidOperationException("Boom.");
        }

        private class ThrowingOnCleanupConsumerProxyBehaviour : ProxyBehaviour
        {
            [Inject] public ScopedLeaf Leaf;
            protected override void OnCleanup() => throw new InvalidOperationException("Boom.");
        }

        private class InterfaceAliasConsumerProxy : Proxy
        {
            [Inject] public IScopedLeaf Leaf;
        }

        private class RecursiveDepConsumerProxy : Proxy
        {
            [Inject] public ScopedWrapperWithPermanentDep Wrapper;
        }

        private class NestedScopedConsumerProxy : Proxy
        {
            [Inject] public ScopedWrapperWithNestedScoped Outer;
            [Inject] public ScopedLeaf DirectLeaf;
        }

        private class NestedProxyDependencyConsumerProxy : Proxy
        {
            [Inject] public ScopedProxyDependency NestedProxy;
            [Inject] public ScopedLeaf DirectLeaf;
        }

        private class GlobalScopedConsumerProxy : Proxy
        {
            [InjectGlobal] public ScopedLeaf InjectedLeaf;
            public ScopedLeaf ResolveLeaf() => Global.Resolve<ScopedLeaf>();
        }

        // ── Test doubles: attribute composition-style entry point ──────────

        [Register(typeof(SeedTestModule), RegisterToLogic = true, Lifecycle = RegistrationLifecycle.Scoped)]
        private sealed class AttributeScopedProxy : Proxy
        {
            public static int OnInitializedTotalCount;
            protected override void OnInitialized() => OnInitializedTotalCount++;
        }

        private sealed class SeedTestModule : MvcModule
        {
        }

        // ── Fields ────────────────────────────────────────────────────────

        private MvcDiContainer _container;
        private MvcMessageBus _messageBus;
        private readonly List<GameObject> _trackedGameObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            ScopedLeaf.ResetIds();
            AttributeScopedProxy.OnInitializedTotalCount = 0;

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

            _messageBus?.Dispose();
            _container?.Dispose();
        }

        // ── Ownership: one dedicated instance per injectee ──────────────────

        [Test]
        public void DistinctProxyInstances_InjectingSameScopedType_ReceiveSeparateInstances()
        {
            _container.Register(new ScopedLeaf()).ToLogic().AsScoped();

            var proxyA = new LeafConsumerProxy();
            var proxyB = new LeafConsumerProxy();

            proxyA.Initialize(typeof(LeafConsumerProxy), _messageBus, _container);
            proxyB.Initialize(typeof(LeafConsumerProxy), _messageBus, _container);

            Assert.That(proxyA.Leaf, Is.Not.Null,
                "A scoped dependency must resolve into a Proxy injectee instead of throwing.");
            Assert.That(proxyB.Leaf, Is.Not.Null,
                "A scoped dependency must resolve into a Proxy injectee instead of throwing.");
            Assert.That(proxyA.Leaf, Is.Not.SameAs(proxyB.Leaf),
                "Ownership is per-injectee: each owning Proxy instance must receive its own dedicated " +
                "scoped instance, never one shared globally across every consumer of the registration.");

            proxyA.Dispose();
            proxyB.Dispose();
        }

        [Test]
        public void DistinctProxyBehaviourInstances_InjectingSameScopedType_ReceiveSeparateInstances()
        {
            _container.Register(new ScopedLeaf()).ToLogic().AsScoped();

            var goA = new GameObject(nameof(LeafConsumerProxyBehaviour) + "A");
            var goB = new GameObject(nameof(LeafConsumerProxyBehaviour) + "B");
            _trackedGameObjects.Add(goA);
            _trackedGameObjects.Add(goB);

            var proxyA = goA.AddComponent<LeafConsumerProxyBehaviour>();
            var proxyB = goB.AddComponent<LeafConsumerProxyBehaviour>();

            proxyA.InitializeGlobal(_container, _messageBus);
            proxyB.InitializeGlobal(_container, _messageBus);

            Assert.That(proxyA.Leaf, Is.Not.Null,
                "A scoped dependency must resolve into a ProxyBehaviour injectee instead of throwing.");
            Assert.That(proxyB.Leaf, Is.Not.Null,
                "A scoped dependency must resolve into a ProxyBehaviour injectee instead of throwing.");
            Assert.That(proxyA.Leaf, Is.Not.SameAs(proxyB.Leaf),
                "Ownership is per-injectee: each owning ProxyBehaviour instance must receive its own " +
                "dedicated scoped instance, never one shared globally across every consumer.");
        }

        // ── Construction: recursive [Inject] resolution on the scoped object itself ──

        [Test]
        public void ScopedDependency_OwnInjectFieldsResolveRecursively()
        {
            var helper = new PermanentHelper();
            _container.Register(helper).ToLogic().AsPermanent();
            _container.Register(new ScopedWrapperWithPermanentDep()).ToLogic().AsScoped();

            var proxy = new RecursiveDepConsumerProxy();
            proxy.Initialize(typeof(RecursiveDepConsumerProxy), _messageBus, _container);

            Assert.That(proxy.Wrapper, Is.Not.Null);
            Assert.That(proxy.Wrapper.Helper, Is.SameAs(helper),
                "A scoped dependency's own [Inject] fields must be resolved recursively from the same " +
                "container, exactly like any normally-registered actor's fields.");

            proxy.Dispose();
        }

        // ── Construction: IMvcLifecycle callback on the scoped object itself ──

        [Test]
        public void ScopedDependency_ImplementingIMvcLifecycle_ReceivesOnInitializedCall()
        {
            _container.Register(new ScopedLeaf()).ToLogic().AsScoped();

            var proxy = new LeafConsumerProxy();
            proxy.Initialize(typeof(LeafConsumerProxy), _messageBus, _container);

            Assert.That(proxy.Leaf, Is.Not.Null);
            Assert.That(proxy.Leaf.OnInitializedCount, Is.EqualTo(1),
                "A scoped dependency implementing IMvcLifecycle must have OnInitialized() called exactly " +
                "once as part of construction, before the instance is handed to the owning Proxy.");

            proxy.Dispose();
        }

        // ── Construction: a scoped Proxy dependency must reuse the real Proxy pipeline ──

        [Test]
        public void ScopedProxyDependency_ConstructedThroughRealProxyPipeline()
        {
            _container.Register(new ScopedLeaf()).ToLogic().AsScoped();
            _container.Register(new ScopedProxyDependency()).ToLogic().AsScoped();

            var proxy = new NestedProxyDependencyConsumerProxy();
            proxy.Initialize(typeof(NestedProxyDependencyConsumerProxy), _messageBus, _container);

            Assert.That(proxy.NestedProxy, Is.Not.Null);
            Assert.That(proxy.NestedProxy.OnInitializedCount, Is.EqualTo(1),
                "A scoped dependency that is itself a Proxy must go through the real Initialize/" +
                "CompleteInitialization pipeline (wiring Messenger/Container/Global and calling " +
                "OnInitialized()) - not a bare Activator.CreateInstance with no lifecycle wiring at all.");
            Assert.That(proxy.NestedProxy.NestedLeaf, Is.SameAs(proxy.DirectLeaf),
                "A scoped Proxy's own dependencies must remain in the top-level injectee's scope; " +
                "creating a second owner scope breaks alias identity and circular graphs.");

            proxy.Dispose();

            Assert.That(proxy.NestedProxy.OnCleanupCount, Is.EqualTo(1),
                "Disposing the owning Proxy must release its nested scoped Proxy dependency through that " +
                "dependency's own real Dispose/OnCleanup path, exactly once.");
        }

        [Test]
        public void GlobalScopedDependency_InjectAndExplicitResolveShareOwnerAndReleaseOnDispose()
        {
            var facade = MvcFacade.FacadeInstance;
            _trackedGameObjects.Add(facade.gameObject);
            MvcFacade.Global.Register(new ScopedLeaf()).ToLogic().AsScoped();

            var proxy = new GlobalScopedConsumerProxy();
            proxy.Initialize(typeof(GlobalScopedConsumerProxy), _messageBus, _container);

            var explicitlyResolved = proxy.ResolveLeaf();
            Assert.That(explicitlyResolved, Is.SameAs(proxy.InjectedLeaf));

            proxy.Dispose();
            Assert.That(explicitlyResolved.IsDisposed, Is.True,
                "Actor teardown must release scoped instances owned in the global container too.");
        }

        // ── Construction: interface-alias scoped registration must build the concrete type ──

        [Test]
        public void InterfaceAliasedScopedRegistration_ResolvesConcreteImplementationInstance()
        {
            _container.Register(new ScopedLeaf()).ToLogicAs<IScopedLeaf>().AsScoped();

            var descriptor = _container.GetScopedRegistration(typeof(IScopedLeaf));
            Assert.That(descriptor, Is.Not.Null,
                "Registration bookkeeping for the alias must exist under the requested interface type.");
            Assert.That(descriptor.ImplementationType, Is.EqualTo(typeof(ScopedLeaf)),
                "The registration descriptor must record the concrete implementation type, not the " +
                "exposed interface, so construction never attempts Activator.CreateInstance(typeof(IFoo)).");

            var proxy = new InterfaceAliasConsumerProxy();
            proxy.Initialize(typeof(InterfaceAliasConsumerProxy), _messageBus, _container);

            Assert.That(proxy.Leaf, Is.Not.Null);
            Assert.That(proxy.Leaf, Is.InstanceOf<ScopedLeaf>(),
                "Resolving a scoped registration exposed only via an interface alias must construct the " +
                "real concrete implementation - today this crashes via Activator.CreateInstance(typeof(IFoo)).");

            proxy.Dispose();
        }

        // ── Construction: two-level-deep nested scoped resolution shares the owner's scope ──

        [Test]
        public void NestedScopedDependency_SharesOwnerScopeWithSiblingInjection()
        {
            _container.Register(new ScopedLeaf()).ToLogic().AsScoped();
            _container.Register(new ScopedWrapperWithNestedScoped()).ToLogic().AsScoped();

            var proxy = new NestedScopedConsumerProxy();
            proxy.Initialize(typeof(NestedScopedConsumerProxy), _messageBus, _container);

            Assert.That(proxy.Outer, Is.Not.Null);
            Assert.That(proxy.DirectLeaf, Is.Not.Null);
            Assert.That(proxy.Outer.NestedLeaf, Is.Not.Null);
            Assert.That(proxy.Outer.NestedLeaf, Is.SameAs(proxy.DirectLeaf),
                "A scoped dependency's own nested scoped [Inject] field must resolve against the SAME " +
                "per-owner scope as a sibling scoped field injected directly on the owning Proxy - the " +
                "owning Proxy is always the scope owner, never the intermediate constructed object. Two " +
                "separate scopes here would silently break the one-dedicated-instance-per-owner guarantee.");

            proxy.Dispose();
        }

        // ── Disposal: exactly-once release on the real Proxy/ProxyBehaviour teardown paths ──

        [Test]
        public void Proxy_ExplicitDispose_ReleasesScopedDependencyExactlyOnce()
        {
            _container.Register(new ScopedLeaf()).ToLogic().AsScoped();

            var proxy = new LeafConsumerProxy();
            proxy.Initialize(typeof(LeafConsumerProxy), _messageBus, _container);

            var leaf = proxy.Leaf;
            Assert.That(leaf, Is.Not.Null);

            proxy.Dispose();

            Assert.That(leaf.IsDisposed, Is.True,
                "Disposing the owning Proxy must dispose its dedicated scoped instance.");
            Assert.That(leaf.OnCleanupCount, Is.EqualTo(1),
                "The scoped instance's OnCleanup (IMvcLifecycle) must run exactly once as part of release.");

            proxy.Dispose(); // second call must be a no-op, per Proxy's own exactly-once disposal guard

            Assert.That(leaf.OnCleanupCount, Is.EqualTo(1),
                "A second Dispose() on the owning Proxy must not release the scoped dependency again.");
        }

        [Test]
        public void ProxyBehaviour_OnDestroy_ReleasesScopedDependencyExactlyOnce()
        {
            _container.Register(new ScopedLeaf()).ToLogic().AsScoped();

            var go = new GameObject(nameof(LeafConsumerProxyBehaviour));
            _trackedGameObjects.Add(go);
            var proxy = go.AddComponent<LeafConsumerProxyBehaviour>();
            proxy.InitializeGlobal(_container, _messageBus);

            var leaf = proxy.Leaf;
            Assert.That(leaf, Is.Not.Null);

            Object.DestroyImmediate(go);
            _trackedGameObjects.Remove(go);

            Assert.That(leaf.IsDisposed, Is.True,
                "Destroying the owning ProxyBehaviour's GameObject must dispose its dedicated scoped instance.");
            Assert.That(leaf.OnCleanupCount, Is.EqualTo(1),
                "The scoped instance's OnCleanup must run exactly once when the owning ProxyBehaviour is destroyed.");
        }

        // ── Disposal: scoped release survives a throwing proxy lifecycle callback ─────────

        [Test]
        public void Proxy_ExplicitDispose_OwnOnCleanupThrows_ScopedDependencyStillReleased()
        {
            // Proxy.Dispose() used to call OnCleanup() with no try/finally,
            // so a throwing override skipped ReleaseScopedInstancesFor entirely, leaking every
            // scoped dependency this proxy owned.
            _container.Register(new ScopedLeaf()).ToLogic().AsScoped();

            var proxy = new ThrowingOnCleanupConsumerProxy();
            proxy.Initialize(typeof(ThrowingOnCleanupConsumerProxy), _messageBus, _container);

            var leaf = proxy.Leaf;
            Assert.That(leaf, Is.Not.Null);

            Assert.Throws<InvalidOperationException>(() => proxy.Dispose(),
                "OnCleanup()'s own exception must still propagate to the caller - this fix must not " +
                "silently swallow it, only guarantee cleanup still happens around it.");

            Assert.That(leaf.IsDisposed, Is.True,
                "The scoped dependency must still be released even though the owning proxy's own " +
                "OnCleanup() threw.");
        }

        [Test]
        public void ProxyBehaviour_OnDestroy_OwnOnCleanupThrows_ScopedDependencyStillReleased()
        {
            _container.Register(new ScopedLeaf()).ToLogic().AsScoped();

            var go = new GameObject(nameof(ThrowingOnCleanupConsumerProxyBehaviour));
            _trackedGameObjects.Add(go);
            var proxy = go.AddComponent<ThrowingOnCleanupConsumerProxyBehaviour>();
            proxy.InitializeGlobal(_container, _messageBus);

            var leaf = proxy.Leaf;
            Assert.That(leaf, Is.Not.Null);

            // Unity logs an uncaught exception thrown from OnDestroy() itself rather than letting it
            // propagate to the caller of DestroyImmediate - expect that log instead of Assert.Throws.
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Boom"));
            Object.DestroyImmediate(go);
            _trackedGameObjects.Remove(go);

            Assert.That(leaf.IsDisposed, Is.True,
                "The scoped dependency must still be released even though the owning ProxyBehaviour's " +
                "own OnCleanup() threw during OnDestroy().");
        }

        // ── Domain isolation: a View-only scoped registration must not leak into Logic injectees ──

        [Test]
        public void ViewOnlyScopedRegistration_InjectedIntoLogicProxy_FailsWithDomainSpecificError()
        {
            _container.Register(new ScopedLeaf()).ToViewAs<IScopedLeaf>().AsScoped();

            var proxy = new InterfaceAliasConsumerProxy();
            proxy.Initialize(typeof(InterfaceAliasConsumerProxy), _messageBus, _container, deferOnInitialized: true);

            var ex = Assert.Throws<InvalidOperationException>(() => proxy.CompleteInitialization(),
                "A scoped registration exposed only to View must not silently resolve for a Logic " +
                "injectee like Proxy - it must fail loudly instead of leaking across domains.");

            Assert.That(ex.Message, Does.Not.Contain("no scoped resolution scope is active"),
                "The failure must come from a real Logic/View domain check, not from today's incidental " +
                "ambient-scope-missing error - that error fires for every scoped injection into a Proxy " +
                "regardless of domain, so it would mask a genuine domain leak once Scoped v2 removes the " +
                "legacy ambient-scope requirement entirely.");
        }

        // ── Composition-style regression: the attribute-registered seed instance must stay inert ──

        [Test]
        public void AttributeRegisteredScopedProxy_SeedInstanceNeverReceivesOnInitialized()
        {
            var moduleGo = new GameObject(nameof(SeedTestModule));
            try
            {
                var module = moduleGo.AddComponent<SeedTestModule>();

                var descriptor = module.DiContainer.GetScopedRegistration(typeof(AttributeScopedProxy));
                Assert.That(descriptor, Is.Not.Null,
                    "The [Register(..., Lifecycle = Scoped)] attribute path must record scoped v2 " +
                    "bookkeeping for the throwaway seed instance's type, same as the code-style " +
                    "AsScoped() entry point.");

                Assert.That(AttributeScopedProxy.OnInitializedTotalCount, Is.EqualTo(0),
                    "The throwaway seed instance built purely to read [Register] metadata must never be " +
                    "tracked or initialized - OnInitialized() must not fire for it. Only a real, owner-" +
                    "resolved instance may ever receive an OnInitialized() call.");
            }
            finally
            {
                Object.DestroyImmediate(moduleGo);
                var facade = MvcFacade.InstanceOrNull;
                if (facade != null) Object.DestroyImmediate(facade.gameObject);
            }
        }
    }
}
