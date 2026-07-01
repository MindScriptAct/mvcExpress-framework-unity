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
            _container.Register(dep).ToView().AsPersistent();

            _mediator.Initialize(_module, _container, _messageBus, deferOnInitialized: false);

            Assert.AreSame(dep, _mediator.InjectedDep,
                "View-scope dependency must be injected (proves BeginViewScope was entered before InjectMembers).");
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
