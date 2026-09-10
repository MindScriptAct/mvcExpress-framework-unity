using NUnit.Framework;
using mvcExpress.Internal.DependencyInjection;
using System;

namespace mvcExpress.Tests
{
    /// <summary>
    /// Test-first specification for the Scoped lifetime redesign.
    /// See InternalSpecs/Architecture/SCOPED_LIFETIME_REDESIGN.md for the full design contract.
    /// </summary>
    public class MvcDiContainer_ScopedLifetime_UnitTests
    {
        private MvcDiContainer _container;

        // --- Test doubles -----------------------------------------------------------------

        private interface IScopedLogicMarker { }
        private interface IScopedViewMarker { }

        // Registered via .ToLogicAs<IScopedLogicMarker>().ToViewAs<IScopedViewMarker>() - one
        // instance, two aliases, must resolve to ONE registrationId (design contract item 1).
        private class DualExposedScopedService : IScopedLogicMarker, IScopedViewMarker { }

        private class LogicOnlyScopedService { }
        private class ViewOnlyScopedService { }
        private class OtherScopedService { }

        private class DisposableScopedItem : IDisposable
        {
            public bool IsDisposed { get; private set; }
            public void Dispose() => IsDisposed = true;
        }

        private class FailsFirstInitialization : IMvcLifecycle, IDisposable
        {
            public static int InitializationAttempts;
            public bool IsDisposed { get; private set; }

            public void OnInitialized()
            {
                if (InitializationAttempts++ == 0)
                    throw new InvalidOperationException("Expected first-attempt failure.");
            }

            public void OnCleanup() { }
            public void Dispose() => IsDisposed = true;
        }

        private interface IListScopedItem { }
        private class ListScopedItem : IListScopedItem { }

        private class MockMonoBehaviourScopedService : UnityEngine.MonoBehaviour { }

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

        // --- Item 1: aliased registration shares one RegistrationId ------------------------

        [Test]
        public void RegisterScopedIdentity_DualChainAliasedToLogicAndView_SharesOneRegistrationId()
        {
            _container.Register(new DualExposedScopedService())
                .ToLogicAs<IScopedLogicMarker>()
                .ToViewAs<IScopedViewMarker>()
                .AsScoped();

            var logicDescriptor = _container.GetScopedRegistration(typeof(IScopedLogicMarker));
            var viewDescriptor = _container.GetScopedRegistration(typeof(IScopedViewMarker));

            Assert.That(logicDescriptor, Is.Not.Null, "Logic alias must have a registration descriptor.");
            Assert.That(viewDescriptor, Is.Not.Null, "View alias must have a registration descriptor.");
            Assert.That(logicDescriptor.RegistrationId, Is.EqualTo(viewDescriptor.RegistrationId),
                "Both aliases came from the same .AsScoped() builder chain and must share one registrationId " +
                "(design contract: identity = registrationId, not Type).");
            Assert.That(logicDescriptor.ImplementationType, Is.EqualTo(typeof(DualExposedScopedService)),
                "ImplementationType must be the concrete runtime type, never the requested interface - " +
                "resolution needs a constructible type (Activator.CreateInstance can't build an interface).");
        }

        [Test]
        public void RuntimeTypeRegistration_InterfaceExposure_ConstructsConcreteRuntimeType()
        {
            object seed = new DualExposedScopedService();
            _container.Register(seed, typeof(IScopedLogicMarker)).ToLogic().AsScoped();

            var descriptor = _container.GetScopedRegistration(typeof(IScopedLogicMarker));
            Assert.That(descriptor.ImplementationType, Is.EqualTo(typeof(DualExposedScopedService)));

            Assert.That(
                _container.TryResolveScopedForOwner(
                    typeof(IScopedLogicMarker), new object(), useViewScope: false, out var resolved),
                Is.True);
            Assert.That(resolved, Is.InstanceOf<DualExposedScopedService>());
        }

        [Test]
        public void DualDomainRegistration_DoesNotExposeEachAliasToTheOtherDomain()
        {
            _container.Register(new DualExposedScopedService())
                .ToLogicAs<IScopedLogicMarker>()
                .ToViewAs<IScopedViewMarker>()
                .AsScoped();

            var owner = new object();
            Assert.Throws<InvalidOperationException>(() =>
                _container.TryResolveScopedForOwner(
                    typeof(IScopedLogicMarker), owner, useViewScope: true, out _));
            Assert.Throws<InvalidOperationException>(() =>
                _container.TryResolveScopedForOwner(
                    typeof(IScopedViewMarker), owner, useViewScope: false, out _));
        }

        [Test]
        public void FailedInitialization_DoesNotPoisonOwnerScopeCache()
        {
            FailsFirstInitialization.InitializationAttempts = 0;
            _container.Register(new FailsFirstInitialization()).ToLogic().AsScoped();
            var owner = new object();

            Assert.Throws<InvalidOperationException>(() =>
                _container.TryResolveScopedForOwner(
                    typeof(FailsFirstInitialization), owner, useViewScope: false, out _));

            Assert.That(
                _container.TryResolveScopedForOwner(
                    typeof(FailsFirstInitialization), owner, useViewScope: false, out var resolved),
                Is.True);
            Assert.That(resolved, Is.TypeOf<FailsFirstInitialization>());
            Assert.That(((FailsFirstInitialization)resolved).IsDisposed, Is.False);
        }

        [Test]
        public void UnregisterScopedAlias_RemovesAliasAndKeepsOtherAliasesUsable()
        {
            _container.Register(new DualExposedScopedService())
                .ToLogicAs<IScopedLogicMarker>()
                .ToViewAs<IScopedViewMarker>()
                .AsScoped();

            _container.Unregister<IScopedLogicMarker>();

            Assert.That(
                _container.TryResolveScopedForOwner(
                    typeof(IScopedLogicMarker), new object(), useViewScope: false, out _),
                Is.False);
            Assert.That(
                _container.TryResolveScopedForOwner(
                    typeof(IScopedViewMarker), new object(), useViewScope: true, out var remaining),
                Is.True);
            Assert.That(remaining, Is.InstanceOf<DualExposedScopedService>());
        }

        // --- Item 2: two separate .AsScoped() chains get different ids --------------------

        [Test]
        public void RegisterScopedIdentity_TwoSeparateScopedRegistrations_GetDifferentRegistrationIds()
        {
            _container.Register(new LogicOnlyScopedService()).ToLogic().AsScoped();
            _container.Register(new OtherScopedService()).ToLogic().AsScoped();

            var first = _container.GetScopedRegistration(typeof(LogicOnlyScopedService));
            var second = _container.GetScopedRegistration(typeof(OtherScopedService));

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(first.RegistrationId, Is.Not.EqualTo(second.RegistrationId),
                "Two independent .AsScoped() builder chains must never collide on registrationId, " +
                "even for unrelated/same-shaped types.");
        }

        // --- Item 3: ExposedToLogic/ExposedToView reflect what was actually registered -----

        [Test]
        public void ScopedRegistration_LogicOnlyChain_ExposedToLogicTrueAndViewFalse()
        {
            _container.Register(new LogicOnlyScopedService()).ToLogic().AsScoped();

            var descriptor = _container.GetScopedRegistration(typeof(LogicOnlyScopedService));

            Assert.That(descriptor.ExposedToLogic, Is.True);
            Assert.That(descriptor.ExposedToView, Is.False);
        }

        [Test]
        public void ScopedRegistration_ViewOnlyChain_ExposedToViewTrueAndLogicFalse()
        {
            _container.Register(new ViewOnlyScopedService()).ToView().AsScoped();

            var descriptor = _container.GetScopedRegistration(typeof(ViewOnlyScopedService));

            Assert.That(descriptor.ExposedToView, Is.True);
            Assert.That(descriptor.ExposedToLogic, Is.False);
        }

        [Test]
        public void ScopedRegistration_DualChain_ExposedToBothLogicAndView()
        {
            _container.Register(new DualExposedScopedService())
                .ToLogicAs<IScopedLogicMarker>()
                .ToViewAs<IScopedViewMarker>()
                .AsScoped();

            var descriptor = _container.GetScopedRegistration(typeof(IScopedLogicMarker));

            Assert.That(descriptor.ExposedToLogic, Is.True);
            Assert.That(descriptor.ExposedToView, Is.True);
        }

        // --- Item 4/5: per-owner resolution identity --------------------------------------

        [Test]
        public void TryResolveScopedForOwner_SameOwnerTwice_ReturnsSameInstance()
        {
            _container.Register(new LogicOnlyScopedService()).ToLogic().AsScoped();
            var owner = new object();

            _container.TryResolveScopedForOwner(typeof(LogicOnlyScopedService), owner, useViewScope: false, out var first);
            _container.TryResolveScopedForOwner(typeof(LogicOnlyScopedService), owner, useViewScope: false, out var second);

            Assert.That(second, Is.SameAs(first),
                "Resolving the same registration for the same scopeOwner twice must return the same instance " +
                "(ownership is per-injectee, memoized by registrationId on that owner's ScopedInstanceScope).");
        }

        [Test]
        public void TryResolveScopedForOwner_TwoDifferentOwners_ReturnDifferentInstances()
        {
            _container.Register(new LogicOnlyScopedService()).ToLogic().AsScoped();
            var ownerA = new object();
            var ownerB = new object();

            _container.TryResolveScopedForOwner(typeof(LogicOnlyScopedService), ownerA, useViewScope: false, out var forA);
            _container.TryResolveScopedForOwner(typeof(LogicOnlyScopedService), ownerB, useViewScope: false, out var forB);

            Assert.That(forB, Is.Not.SameAs(forA),
                "Every consumer that resolves a scoped type gets its own dedicated instance - " +
                "nobody else can see it (design contract 'Target semantics').");
        }

        // --- Item 6: disposal on release --------------------------------------------------

        [Test]
        public void ReleaseScopedInstancesFor_DisposesEveryInstanceOwnerConstructed_AndDoesNotSilentlyReuseAfterRelease()
        {
            _container.Register(new DisposableScopedItem()).ToLogic().AsScoped();
            var owner = new object();

            _container.TryResolveScopedForOwner(typeof(DisposableScopedItem), owner, useViewScope: false, out var resolved);
            var instance = (DisposableScopedItem)resolved;

            _container.ReleaseScopedInstancesFor(owner);

            Assert.That(instance.IsDisposed, Is.True,
                "Releasing an owner's scope must dispose every IDisposable instance that owner constructed.");

            // Defensive check only - this resolve shouldn't normally happen post-teardown, but if
            // it does, the already-disposed instance must never be handed back silently.
            _container.TryResolveScopedForOwner(typeof(DisposableScopedItem), owner, useViewScope: false, out var afterRelease);
            Assert.That(afterRelease, Is.Not.SameAs(instance),
                "A resolve for an owner whose scope was already released must not silently return the " +
                "disposed instance.");
        }

        // --- Item 7: REGRESSION - MonoBehaviour guard remains intact ----------------------

        [Test]
        public void Regression_AsScopedOnMonoBehaviour_StillThrowsInvalidOperationException()
        {
            var go = new UnityEngine.GameObject(nameof(MockMonoBehaviourScopedService));
            try
            {
                var service = go.AddComponent<MockMonoBehaviourScopedService>();

                var ex = Assert.Throws<InvalidOperationException>(
                    () => _container.Register(service).ToLogic().AsScoped());

                Assert.That(ex.Message, Does.Contain("AsScoped() is not supported for MonoBehaviour"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // --- Item 8: scoped lists must throw at registration ------------------------------

        [Test]
        public void ToLogicListThenAsScoped_ShouldThrowInvalidOperationException_ButCurrentlyDoesNotThrow()
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                _container.Register<IListScopedItem>(new ListScopedItem())
                    .ToLogicList<IListScopedItem>()
                    .AsScoped();
            }, "Scoped list registrations have no design for scoped collections yet - registration must " +
               "hard-fail loudly instead of silently registering nothing.");
        }
    }
}
