using System;
using System.Reflection;
using NUnit.Framework;
using mvcExpress;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;

namespace mvcExpress.Tests
{
    [TestFixture]
    public class ProxyTests
    {
        // ── Test doubles ──────────────────────────────────────────────────────

        // Concrete proxy stub that tracks lifecycle call counts.
        private class TestProxy : Proxy
        {
            public int OnInitializedCallCount { get; private set; }
            public int OnCleanupCallCount     { get; private set; }

            protected override void OnInitialized() => OnInitializedCallCount++;
            protected override void OnCleanup()     => OnCleanupCallCount++;
        }

        // ── Fields ────────────────────────────────────────────────────────────

        private MvcDiContainer _container;
        private MvcMessageBus  _messageBus;
        private TestProxy      _proxy;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            _container  = new MvcDiContainer();
            _messageBus = new MvcMessageBus();
            _proxy      = new TestProxy();
        }

        [TearDown]
        public void TearDown()
        {
            _messageBus?.Dispose();
            _container?.Dispose();
            // Do NOT call _proxy.Dispose() here - individual tests manage disposal
            // to avoid double-disposal or interference with the finalizer test.
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Calls the protected <c>Dispose(bool)</c> overload via reflection so the
        /// finalizer path (disposing=false) can be tested without waiting for the GC.
        /// </summary>
        private static void InvokeDisposeProtected(Proxy proxy, bool disposing)
        {
            var method = typeof(Proxy).GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(bool) },
                null);

            Assert.IsNotNull(method, "Protected Dispose(bool) method must exist on Proxy.");
            method.Invoke(proxy, new object[] { disposing });
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Test]
        public void InitializationWithDeferral_SetsUpApisButDefersUserInitialization()
        {
            // Act - deferred init: framework wiring happens but OnInitialized must NOT fire.
            _proxy.Initialize(typeof(TestProxy), _messageBus, _container, deferOnInitialized: true);

            // Assert: ModuleType is wired up immediately.
            Assert.AreEqual(typeof(TestProxy), _proxy.ModuleType,
                "ModuleType must be set immediately by Initialize regardless of deferral.");

            // Assert: user callback has NOT been invoked yet.
            Assert.AreEqual(0, _proxy.OnInitializedCallCount,
                "OnInitialized must NOT be called when deferOnInitialized is true.");

            _proxy.Dispose(); // cleanup
        }

        [Test]
        public void CompleteInitialization_InjectsDependenciesAndTriggersUserLogic()
        {
            // Arrange - defer so we can call CompleteInitialization manually.
            _proxy.Initialize(typeof(TestProxy), _messageBus, _container, deferOnInitialized: true);
            Assert.AreEqual(0, _proxy.OnInitializedCallCount,
                "Precondition: OnInitialized must not have been called yet.");

            // Act.
            _proxy.CompleteInitialization();

            // Assert: OnInitialized was called exactly once.
            Assert.AreEqual(1, _proxy.OnInitializedCallCount,
                "OnInitialized must be called exactly once after CompleteInitialization.");

            // Assert: calling CompleteInitialization again is a no-op (idempotent).
            _proxy.CompleteInitialization();
            Assert.AreEqual(1, _proxy.OnInitializedCallCount,
                "Calling CompleteInitialization a second time must not call OnInitialized again.");

            _proxy.Dispose(); // cleanup
        }

        [Test]
        public void DisposalAndCleanup_CleansUpResourcesAndTriggersTeardown()
        {
            // Arrange - fully initialize the proxy.
            _proxy.Initialize(typeof(TestProxy), _messageBus, _container, deferOnInitialized: false);

            // Act - first dispose.
            _proxy.Dispose();

            // Assert: OnCleanup was called exactly once.
            Assert.AreEqual(1, _proxy.OnCleanupCallCount,
                "OnCleanup must be called exactly once on the first Dispose().");

            // Act - second dispose must be a no-op.
            _proxy.Dispose();

            Assert.AreEqual(1, _proxy.OnCleanupCallCount,
                "OnCleanup must NOT be called again on subsequent Dispose() calls.");
        }

        [Test]
        public void Dispose_BeforeInitialization_DoesNotCallUserCleanup()
        {
            _proxy.Dispose();

            Assert.AreEqual(0, _proxy.OnCleanupCallCount,
                "OnCleanup must not run when a proxy was never initialized.");
        }

        [Test]
        public void Initialize_WithNullArguments_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => _proxy.Initialize((Type)null, _messageBus, _container));
            Assert.Throws<ArgumentNullException>(
                () => _proxy.Initialize(typeof(TestProxy), null, _container));
            Assert.Throws<ArgumentNullException>(
                () => _proxy.Initialize(typeof(TestProxy), _messageBus, null));
        }

        [Test]
        public void Initialize_AfterDispose_ThrowsObjectDisposedException()
        {
            _proxy.Dispose();

            Assert.Throws<ObjectDisposedException>(
                () => _proxy.Initialize(typeof(TestProxy), _messageBus, _container));
        }

        [Test]
        public void FinalizerSafetyNet_DoesNotCallOnCleanupOnFinalizerPath()
        {
            // Arrange - initialize so the proxy has live internal state.
            _proxy.Initialize(typeof(TestProxy), _messageBus, _container, deferOnInitialized: false);

            // Act - simulate the finalizer path by calling Dispose(false) via reflection.
            // This must NOT call OnCleanup (managed resources must not be touched from the finalizer).
            Assert.DoesNotThrow(
                () => InvokeDisposeProtected(_proxy, disposing: false),
                "Dispose(false) must not throw even if the proxy was never explicitly disposed.");

            Assert.AreEqual(0, _proxy.OnCleanupCallCount,
                "OnCleanup must NOT be called when Dispose(false) is invoked (finalizer path).");

            // Calling public Dispose() after Dispose(false) must also be a safe no-op
            // because the proxy was already marked as disposed internally.
            Assert.DoesNotThrow(() => _proxy.Dispose(),
                "Public Dispose() after the finalizer path must not throw.");
        }
    }
}
