using System;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using mvcExpress;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;

namespace mvcExpress.Tests
{
    [TestFixture]
    public class MediatorBehaviourTests
    {
        // ── Test doubles ──────────────────────────────────────────────────────

        private class ViewScopeDependency {}

        // Concrete mediator that exposes lifecycle call counts and an injectable dep.
        private class TestMediator : MediatorBehaviour
        {
            // optional=true so tests that don't register it don't throw
            [Inject(true)]
            public ViewScopeDependency InjectedDep { get; set; }

            public int OnInitializedCallCount { get; private set; }
            public int OnCleanupCallCount { get; private set; }

            protected override void OnInitialized() => OnInitializedCallCount++;
            protected override void OnCleanup()     => OnCleanupCallCount++;

            // Lets a test probe Container.TryResolve at any point after Initialize()
            // has run, in particular AFTER CompleteInitialization's BeginViewScope
            // using-block has already exited (i.e. outside OnInitialized() entirely).
            public bool TryResolveViewDependencyNow(out ViewScopeDependency value)
            {
                return Container.TryResolve(out value);
            }
        }

        // Mediator whose OnEnable() probes Container.TryResolve and records the result,
        // so a test can assert on it afterward. Unity invokes OnEnable through its own
        // engine lifecycle (not through MediatorBehaviour.Initialize), so this reproduces
        // real-world call sites - OnEnable, Update, coroutines, click handlers - that run
        // outside the synchronous extent of CompleteInitialization's BeginViewScope window.
        private class OnEnableProbeMediator : MediatorBehaviour
        {
            public bool? OnEnableTryResolveResult { get; private set; }
            public ViewScopeDependency OnEnableResolvedValue { get; private set; }

            private void OnEnable()
            {
                OnEnableTryResolveResult = Container.TryResolve<ViewScopeDependency>(out var value);
                OnEnableResolvedValue = value;
            }
        }

        // Module stub that skips Awake/OnDestroy entirely so the framework singleton
        // (MvcFacade) is never created. Initialize() only calls module.GetType() and
        // module.ModuleType - both work on any live MonoBehaviour with no init required.
        private class NoInitModule : MvcModule
        {
            protected override void Awake()     {} // intentionally empty - no MvcFacade
            protected override void OnDestroy() {} // intentionally empty - no UnregisterModule
        }

        // ── Fields ────────────────────────────────────────────────────────────

        private GameObject _moduleGo;
        private NoInitModule _module;
        private GameObject _go;
        private TestMediator _mediator;
        private MvcDiContainer _container;
        private MvcMessageBus _messageBus;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            // Module is only created to supply a non-null MvcModule for Initialize().
            // Awake is empty so no side effects.
            _moduleGo = new GameObject("NoInitModule");
            _module = _moduleGo.AddComponent<NoInitModule>();

            _go = new GameObject("TestMediator");
            _mediator = _go.AddComponent<TestMediator>();
            _container = new MvcDiContainer();
            _messageBus = new MvcMessageBus();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)        { Object.DestroyImmediate(_go);       _go = null; }
            if (_moduleGo != null)  { Object.DestroyImmediate(_moduleGo); _moduleGo = null; }
            _messageBus?.Dispose();
            _container?.Dispose();
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Test]
        public void Initialize_NotDeferred_CallsOnInitializedImmediately()
        {
            _mediator.Initialize(_module, _container, _messageBus, deferOnInitialized: false);

            Assert.AreEqual(1, _mediator.OnInitializedCallCount,
                "OnInitialized must fire immediately when deferOnInitialized is false.");
        }

        [Test]
        public void Initialize_NotDeferred_InjectsViewScopeDependency()
        {
            // Register dependency only in the view scope
            var dep = new ViewScopeDependency();
            _container.Register(dep).ToView().AsPermanent();

            _mediator.Initialize(_module, _container, _messageBus, deferOnInitialized: false);

            Assert.AreSame(dep, _mediator.InjectedDep,
                "View-scope dependency must be injected (proves BeginViewScope was entered before InjectMembers).");
        }

        [Test]
        public void Container_TryResolve_FromOnEnable_FailsToFindViewScopeDependency_EvenThoughRegistered()
        {
            // Register the dependency in the VIEW partition FIRST, so the container
            // genuinely contains it before the probe mediator's OnEnable ever runs -
            // this isolates the scope-selection bug from an unrelated ordering/
            // missing-registration failure.
            var dep = new ViewScopeDependency();
            _container.Register(dep).ToView().AsPermanent();

            // Replace the SetUp-created TestMediator with a fresh, inactive GameObject
            // so this test can control precisely when Unity invokes Awake/OnEnable:
            // Unity defers both until SetActive(true) is called on an inactive object.
            Object.DestroyImmediate(_go);
            _go = new GameObject("OnEnableProbeMediator");
            _go.SetActive(false);
            var probe = _go.AddComponent<OnEnableProbeMediator>();

            // Fully link the mediator and run CompleteInitialization - entering AND
            // exiting BeginViewScope's using-block - before Unity ever calls Awake/
            // OnEnable on this component for the first time. This mirrors the real
            // Unity ordering where a mediator's GameObject is already active in a
            // scene and OnEnable fires independently of when the framework attaches it.
            probe.Initialize(_module, _container, _messageBus, deferOnInitialized: false);

            // Unity now calls Awake then OnEnable for the first time, strictly AFTER
            // the BeginViewScope window has already closed.
            _go.SetActive(true);

            // FAILURE SCENARIO this proves: a mediator is a view-layer actor for its
            // whole lifetime, not just during the synchronous extent of OnInitialized().
            // Container.TryResolve called from OnEnable should therefore still find a
            // genuinely-registered view-scope dependency. Today it does not: the
            // ambient IsViewScope flag has already reverted to false by the time
            // OnEnable runs, so TryResolve silently queries the logic partition
            // instead and reports "not found" even though the dependency exists.
            Assert.IsTrue(probe.OnEnableTryResolveResult.GetValueOrDefault(),
                "Container.TryResolve from OnEnable must find a genuinely-registered view-scope " +
                "dependency. BUG: the ambient IsViewScope flag only stays true for the synchronous " +
                "duration of CompleteInitialization's BeginViewScope block, so any later call - " +
                "including OnEnable - silently resolves against the logic scope instead.");
            Assert.AreSame(dep, probe.OnEnableResolvedValue,
                "The value captured during OnEnable must be the exact instance registered in the view scope.");
        }

        [Test]
        public void Container_TryResolve_AfterOnInitializedCompletes_FailsToFindViewScopeDependency_EvenThoughRegistered()
        {
            // Register the dependency in the VIEW partition before initializing, so the
            // container genuinely contains it - isolating the scope-selection bug from
            // an unrelated ordering/missing-registration failure.
            var dep = new ViewScopeDependency();
            _container.Register(dep).ToView().AsPermanent();

            _mediator.Initialize(_module, _container, _messageBus, deferOnInitialized: false);
            // CompleteInitialization's BeginViewScope using-block has already exited by
            // the time Initialize() returns - this call happens entirely outside the
            // window, e.g. as if made later from Update(), a coroutine, or a click handler.

            bool result = _mediator.TryResolveViewDependencyNow(out var value);

            Assert.IsTrue(result,
                "Container.TryResolve, called after OnInitialized() has already returned, must still " +
                "find a genuinely-registered view-scope dependency - a mediator remains a view-layer " +
                "actor for its whole lifetime, not just during OnInitialized(). BUG: the ambient " +
                "IsViewScope flag has already reverted to false by this point, so TryResolve queries " +
                "the logic partition instead and reports \"not found\".");
            Assert.AreSame(dep, value,
                "The resolved value must be the exact instance registered in the view scope.");
        }

        [Test]
        public void Initialize_Deferred_OnInitializedNotCalledUntilComplete()
        {
            _mediator.Initialize(_module, _container, _messageBus, deferOnInitialized: true);

            Assert.AreEqual(0, _mediator.OnInitializedCallCount,
                "OnInitialized must NOT fire when deferOnInitialized is true.");

            _mediator.CompleteInitialization();

            Assert.AreEqual(1, _mediator.OnInitializedCallCount,
                "OnInitialized must fire after CompleteInitialization is called.");
        }

        [Test]
        public void CompleteInitialization_IsIdempotent_OnInitializedCalledOnlyOnce()
        {
            _mediator.Initialize(_module, _container, _messageBus, deferOnInitialized: true);
            _mediator.CompleteInitialization();
            _mediator.CompleteInitialization(); // second call must be a no-op

            Assert.AreEqual(1, _mediator.OnInitializedCallCount,
                "OnInitialized must not fire more than once even when CompleteInitialization is called twice.");
        }

        [Test]
        public void OnDestroy_UnsubscribesAllTrackedSubscriptions()
        {
            _mediator.Initialize(_module, _container, _messageBus, deferOnInitialized: false);

            // Manually track a subscription so we can assert it was cleaned up.
            bool unsubscribeCalled = false;
            _mediator.SubscriptionTracker.Track(
                typeof(object), _mediator, default(SubscriptionToken), 0,
                _ => { unsubscribeCalled = true; });

            Assert.AreEqual(1, _mediator.SubscriptionTracker.Count);

            // Destroy triggers OnDestroy → CleanupMediator → SubscriptionTracker.UnsubscribeAll
            Object.DestroyImmediate(_go);
            _go = null; // prevent TearDown from double-destroying

            Assert.IsTrue(unsubscribeCalled,
                "Tracked unsubscribe action must be invoked when the mediator is destroyed.");
        }

        [Test]
        public void OnDestroy_CallsOnCleanup()
        {
            _mediator.Initialize(_module, _container, _messageBus, deferOnInitialized: false);

            Object.DestroyImmediate(_go);
            _go = null;

            Assert.AreEqual(1, _mediator.OnCleanupCallCount,
                "OnCleanup must be called exactly once when the mediator is destroyed.");
        }

        [Test]
        public void CleanupMediator_IsIdempotent_OnCleanupCalledOnlyOnce()
        {
            _mediator.Initialize(_module, _container, _messageBus, deferOnInitialized: false);

            _mediator.CleanupMediator();
            _mediator.CleanupMediator(); // second call must be a no-op

            Assert.AreEqual(1, _mediator.OnCleanupCallCount,
                "OnCleanup must not fire more than once even when CleanupMediator is called twice.");
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Test]
        public void Start_WhenNotInitialized_LogsWarning()
        {
            // Mediator is deliberately NOT initialized (_dependenciesLinked remains false)
            var startMethod = typeof(MediatorBehaviour)
                .GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic);

            if (startMethod == null)
            {
                Assert.Ignore("Start() only exists in UNITY_EDITOR or DEVELOPMENT_BUILD builds.");
                return;
            }

            LogAssert.Expect(LogType.Warning, new Regex("not initialized"));
            startMethod.Invoke(_mediator, null);
        }
#endif
    }
}
