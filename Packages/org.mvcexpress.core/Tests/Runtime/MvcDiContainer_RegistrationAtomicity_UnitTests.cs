using System;
using mvcExpress.Internal.DependencyInjection;
using NUnit.Framework;
using UnityEngine;

namespace mvcExpress.Tests
{
    // Regression coverage: RegisterInternal's MonoBehaviour-vs-scoped-type
    // conflict check used to run AFTER phase 2 (direct keys) and phase 3 (list bindings) had already
    // committed, so a rejected registration could leave keys/lists committed anyway - a partial-commit
    // atomicity violation. Investigation found the check was actually unreachable: phase 1's own
    // IsScoped(type) check (unconditional, pre-commit) always threw first for the identical trigger
    // condition, with a differently-worded but equally accurate message - so the post-commit check was
    // removed rather than relocated. These tests lock in that a MonoBehaviour registration colliding
    // with an already-scoped type throws before anything commits, and that a multi-type chain rolls
    // back completely, not just the one colliding key.
    [TestFixture]
    public class MvcDiContainer_RegistrationAtomicity_UnitTests
    {
        private interface IScopedMarker { }
        private interface IUnrelatedMarker { }

        private sealed class PlainScopedSeed : IScopedMarker { }

        private sealed class MockMonoBehaviour : MonoBehaviour, IScopedMarker, IUnrelatedMarker { }

        private MvcDiContainer _container;
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _container = new MvcDiContainer();
        }

        [TearDown]
        public void TearDown()
        {
            _container?.Dispose();
            if (_go != null)
            {
                UnityEngine.Object.DestroyImmediate(_go);
                _go = null;
            }
        }

        [Test]
        public void MonoBehaviourRegistration_TypeAlreadyScoped_ThrowsBeforeAnyCommit()
        {
            _container.Register(new PlainScopedSeed()).ToLogicAs<IScopedMarker>().AsScoped();

            _go = new GameObject("mock");
            var mb = _go.AddComponent<MockMonoBehaviour>();

            var ex = Assert.Throws<InvalidOperationException>(
                () => _container.Register(mb).ToLogicAs<IScopedMarker>().AsPermanent(),
                "Registering a MonoBehaviour under an already-scoped type key must throw.");
            Assert.That(ex.Message, Does.Contain("already registered as scoped"));

            Assert.Throws<InvalidOperationException>(() => _container.Resolve<MockMonoBehaviour>(),
                "The MonoBehaviour must not be resolvable under its own concrete type either - the " +
                "rejected chain must never have committed anything at all.");
        }

        [Test]
        public void MonoBehaviourRegistration_SecondOfTwoTypesAlreadyScoped_FirstTypeAlsoRolledBack()
        {
            _container.Register(new PlainScopedSeed()).ToLogicAs<IScopedMarker>().AsScoped();

            _go = new GameObject("mock");
            var mb = _go.AddComponent<MockMonoBehaviour>();

            // IUnrelatedMarker is a perfectly valid mapping in isolation; IScopedMarker collides.
            // Phase 1 validates the WHOLE chain before phase 2 commits anything, so IUnrelatedMarker
            // must never become resolvable even though its own individual check would have passed.
            Assert.Throws<InvalidOperationException>(
                () => _container.Register(mb).ToLogicAs<IUnrelatedMarker>().ToLogicAs<IScopedMarker>().AsPermanent());

            Assert.Throws<InvalidOperationException>(() => _container.Resolve<IUnrelatedMarker>(),
                "A failed chain must be all-or-nothing - a non-colliding type mapped earlier in the " +
                "SAME chain must not remain committed just because it was validated before the " +
                "colliding one.");
        }

        [Test]
        public void ScopedSeed_StillResolvableThroughScopedMechanism_AfterRejectedConflictingRegistration()
        {
            _container.Register(new PlainScopedSeed()).ToLogicAs<IScopedMarker>().AsScoped();

            _go = new GameObject("mock");
            var mb = _go.AddComponent<MockMonoBehaviour>();
            Assert.Throws<InvalidOperationException>(
                () => _container.Register(mb).ToLogicAs<IScopedMarker>().AsPermanent());

            // The rejected conflicting registration must not have corrupted the pre-existing scoped
            // registration it collided with.
            Assert.That(_container.IsScoped(typeof(IScopedMarker)), Is.True,
                "The original scoped registration must survive a rejected conflicting registration " +
                "attempt untouched.");
        }
    }
}
