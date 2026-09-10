using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
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

        // Throws from OnInitialized() to exercise the failed-initialization path.
        private class ThrowingOnInitializedProxy : Proxy
        {
            protected override void OnInitialized() => throw new InvalidOperationException("Boom.");
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
        public void CompleteInitialization_OnInitializedThrows_ProxyNotLeftMarkedInitialized()
        {
            var proxy = new ThrowingOnInitializedProxy();
            proxy.Initialize(typeof(ThrowingOnInitializedProxy), _messageBus, _container, deferOnInitialized: true);

            Assert.Throws<InvalidOperationException>(() => proxy.CompleteInitialization());

            var initializedField = typeof(Proxy).GetField("_initialized", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsFalse((bool)initializedField.GetValue(proxy),
                "A Proxy whose OnInitialized() throws must not be left marked as initialized - " +
                "otherwise a later retry silently no-ops instead of re-attempting initialization.");
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

        /// <summary>
        /// Proxy used to declare its own finalizer (<c>~Proxy() =&gt; Dispose(false)</c>) as a "safety net" that
        /// could never actually clean anything up, so every instance paid to be finalizable for no
        /// benefit. This is the structural guarantee that fix relies on - deliberately independent
        /// of GC timing, unlike <see cref="LeakCanary_UndisposedProxyCollected_LogsWarning"/> below.
        /// </summary>
        [Test]
        public void Proxy_DoesNotDeclareItsOwnFinalizer()
        {
            var finalizeMethod = typeof(Proxy).GetMethod(
                "Finalize", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(finalizeMethod, "Precondition: every type has a Finalize method (inherited from object).");
            Assert.AreNotEqual(typeof(Proxy), finalizeMethod.DeclaringType,
                "Proxy must not declare/override Finalize (i.e. must not have a ~Proxy() finalizer). " +
                "If this fails, a finalizer was reintroduced on the base class.");
        }

        // Creates an initialized-but-never-disposed proxy in its own stack frame so the JIT/Mono
        // doesn't extend the local's liveness into the calling test method - matching the pattern
        // WeakEventManager_UnitTests.cs already uses for the same reason.
        private void CreateAndAbandonProxy()
        {
            var leaked = new TestProxy();
            leaked.Initialize(typeof(TestProxy), _messageBus, _container, deferOnInitialized: false);
            // Deliberately never Dispose() - this instance is meant to leak, on purpose, for the test below.
        }

        /// <summary>
        /// The leak-detection canary that replaced Proxy's own finalizer must still catch
        /// the case it exists for - an undisposed Proxy quietly falling out of scope - and log it.
        /// </summary>
        [Test]
        public void LeakCanary_UndisposedProxyCollected_LogsWarning()
        {
            CreateAndAbandonProxy();

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex(@"\[Proxy leak\].*TestProxy"));

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect(); // second pass collects objects promoted to the finalization queue
        }
    }
}
