using NUnit.Framework;
using mvcExpress.Internal.DependencyInjection;
using System;
using System.Collections.Generic;

namespace mvcExpress.Tests
{
    public class MvcDiContainerTests
    {
        private MvcDiContainer _container;

        private interface IMockService {}
        private class MockService : IMockService, IDisposable
        {
            public bool IsDisposed { get; private set; }
            public void Dispose() => IsDisposed = true;
        }

        private class ScopedProxy : Proxy
        {
            public bool CleanupCalled { get; private set; }
            protected override void OnCleanup() => CleanupCalled = true;
        }

        [SetUp]
        public void SetUp()
        {
            _container = new MvcDiContainer();
        }

        [TearDown]
        public void TearDown()
        {
            _container.Dispose();
        }

        [Test]
        public void Register_ToSameScopeTwice_ThrowsInvalidOperationException()
        {
            var service = new MockService();
            _container.Register(service).ToLogic().AsPersistent();

            var ex = Assert.Throws<InvalidOperationException>(() => 
            {
                _container.Register(new MockService()).ToLogic().AsPersistent();
            });
            Assert.That(ex.Message, Does.Contain("already registered"));
        }

        [Test]
        public void Resolve_FromLogicScope_Success()
        {
            var service = new MockService();
            _container.Register(service).ToLogic().AsPersistent();

            var resolved = _container.Resolve<MockService>();
            Assert.That(resolved, Is.SameAs(service));
        }

        [Test]
        public void Resolve_FromViewScope_FailsWhenNotInViewScope()
        {
            var service = new MockService();
            _container.Register(service).ToView().AsPersistent();

            Assert.Throws<InvalidOperationException>(() => _container.Resolve<MockService>());
        }

        [Test]
        public void Resolve_FromViewScope_SuccessWhenInViewScope()
        {
            var service = new MockService();
            _container.Register(service).ToView().AsPersistent();

            using (_container.BeginViewScope())
            {
                var resolved = _container.Resolve<MockService>();
                Assert.That(resolved, Is.SameAs(service));
            }
        }

        [Test]
        public void ToLogicList_ResolvesAsList()
        {
            var service1 = new MockService();
            var service2 = new MockService();

            _container.Register<IMockService>(service1).ToLogicList<IMockService>().AsPersistent();
            _container.Register<IMockService>(service2).ToLogicList<IMockService>().AsPersistent();

            var resolved = _container.Resolve<List<IMockService>>();
            Assert.That(resolved.Count, Is.EqualTo(2));
            Assert.That(resolved, Contains.Item(service1));
            Assert.That(resolved, Contains.Item(service2));
        }

        [Test]
        public void Unregister_DisposesInstance()
        {
            var service = new MockService();
            _container.Register(service).ToLogic().AsTransient();

            _container.Unregister<MockService>();

            Assert.That(service.IsDisposed, Is.True);
            Assert.Throws<InvalidOperationException>(() => _container.Resolve<MockService>());
        }

        [Test]
        public void Clear_ResetsContainerAndDisposes()
        {
            var service = new MockService();
            _container.Register(service).ToLogic().AsTransient();

            _container.Clear();

            Assert.That(service.IsDisposed, Is.True);
            Assert.Throws<InvalidOperationException>(() => _container.Resolve<MockService>());
        }

        [Test]
        public void Unregister_PersistentType_Throws()
        {
            var service = new MockService();
            _container.Register(service).ToLogic().AsPersistent();

            Assert.Throws<InvalidOperationException>(
                () => _container.Unregister<MockService>(),
                "Persistent types must not be unregisterable.");
        }

        // RegistrationBuilder state machine tests

        [Test]
        public void RegistrationBuilder_NoLayerSelected_ThrowsBeforeComplete()
        {
            var service = new MockService();
            var builder = _container.Register(service);

            // AsPersistent without ToLogic/ToView must throw
            Assert.Throws<InvalidOperationException>(() => builder.AsPersistent(),
                "Builder must require at least one layer before completing registration.");
        }

        [Test]
        public void RegistrationBuilder_AlreadyCompleted_ThrowsOnFurtherCalls()
        {
            var service = new MockService();
            var builder = _container.Register(service).ToLogic();
            builder.AsPersistent(); // completes the builder

            // Any call after completion must throw
            Assert.Throws<InvalidOperationException>(() => builder.ToView(),
                "Builder must guard against modifications after completion.");
        }

        [Test]
        public void RegistrationBuilder_LayerMixing_RegistersToBothScopes()
        {
            var service = new MockService();
            _container.Register(service).ToLogicAs<IMockService>().ToView().AsPersistent();

            // Logic scope: resolvable as IMockService
            var logicResolved = _container.Resolve<IMockService>();
            Assert.That(logicResolved, Is.SameAs(service));

            // View scope: resolvable as concrete type
            using (_container.BeginViewScope())
            {
                var viewResolved = _container.Resolve<MockService>();
                Assert.That(viewResolved, Is.SameAs(service));
            }
        }

        [Test]
        public void RegistrationBuilder_DualRegistrationFails_RollsBackLogic()
        {
            // First register MockService to view scope
            var existing = new MockService();
            _container.Register(existing).ToView().AsPersistent();

            // Now try to dual-register a second instance to both logic and view.
            // View registration fails (duplicate). Logic registration must be rolled back.
            var second = new MockService();
            Assert.Throws<InvalidOperationException>(
                () => _container.Register(second).ToLogic().ToView().AsPersistent(),
                "Dual registration must fail when view scope already has the type.");

            // Logic scope must still be empty (rollback occurred)
            Assert.Throws<InvalidOperationException>(
                () => _container.Resolve<MockService>(),
                "Logic scope must not contain the second instance after rollback.");

            // View scope must still return the original instance
            using (_container.BeginViewScope())
            {
                var resolved = _container.Resolve<MockService>();
                Assert.That(resolved, Is.SameAs(existing),
                    "View scope must still have the original instance after failed dual-registration.");
            }
        }

        [Test]
        public void RegistrationBuilder_AsScopedOnNonProxy_ThrowsInvalidOperationException()
        {
            var service = new MockService();

            var ex = Assert.Throws<InvalidOperationException>(
                () => _container.Register(service).ToLogic().AsScoped());

            Assert.That(ex.Message, Does.Contain("AsScoped() is only supported"));
        }

        [Test]
        public void RegistrationBuilder_AsScopedAfterInstanceRegistration_ThrowsInvalidOperationException()
        {
            _container.Register(new ScopedProxy()).ToLogic().AsPersistent();

            var ex = Assert.Throws<InvalidOperationException>(
                () => _container.Register(new ScopedProxy()).ToLogic().AsScoped());

            Assert.That(ex.Message, Does.Contain("already registered as an instance"));
        }

        [Test]
        public void Resolve_ScopedProxyWithinScopedResolutionScope_ReusesAndDisposesInstance()
        {
            _container.Register(new ScopedProxy()).ToLogic().AsScoped();

            Assert.That(_container.IsScoped(typeof(ScopedProxy)), Is.True);
            Assert.Throws<InvalidOperationException>(() => _container.Resolve<ScopedProxy>(),
                "Scoped dependencies must require an active scoped resolution context.");

            ScopedProxy first;
            using (_container.BeginScopedResolutionScope())
            {
                first = _container.Resolve<ScopedProxy>();
                var second = _container.Resolve<ScopedProxy>();

                Assert.That(second, Is.SameAs(first),
                    "Scoped dependencies must be reused within one scoped resolution context.");
                Assert.That(first.CleanupCalled, Is.False);
            }

            Assert.Throws<ObjectDisposedException>(
                () => first.Initialize(typeof(ScopedProxy), null, _container),
                "Disposing the scoped resolution token must dispose scoped instances.");
            Assert.That(first.CleanupCalled, Is.False,
                "Scoped proxies created only for resolution were never initialized, so user cleanup must not run.");
        }
    }
}
