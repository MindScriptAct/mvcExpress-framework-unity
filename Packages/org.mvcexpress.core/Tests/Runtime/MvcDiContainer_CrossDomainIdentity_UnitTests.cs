using System;
using mvcExpress.Internal.DependencyInjection;
using NUnit.Framework;

namespace mvcExpress.Tests
{
    // Regression coverage: _transientTypes and UnregisterInternal key
    // per-type state without a domain component, even though nothing previously stopped two
    // SEPARATE Register(...) calls from independently claiming the same Type in Logic and View
    // with different instances/lifetimes. That let unregistering one domain silently corrupt or
    // destroy the other's unrelated registration. Fixed (per the maintainer's chosen "Option 2")
    // by rejecting a second, independent registration of the same type under the other domain at
    // registration time - a type's registration identity now spans both domains, closing the gap
    // at the source rather than making the per-type state domain-aware.
    [TestFixture]
    public class MvcDiContainer_CrossDomainIdentity_UnitTests
    {
        private interface IThing { }
        private sealed class ThingA : IThing { }
        private sealed class ThingB : IThing { }

        private MvcDiContainer _container;

        [SetUp]
        public void SetUp()
        {
            _container = new MvcDiContainer();
        }

        [TearDown]
        public void TearDown()
        {
            _container?.Dispose();
        }

        [Test]
        public void Register_DifferentInstanceToOppositeDomain_ConcreteType_Throws()
        {
            var logicInstance = new ThingA();
            _container.Register(logicInstance).ToLogic().AsPermanent();

            var viewInstance = new ThingA();
            var ex = Assert.Throws<InvalidOperationException>(
                () => _container.Register(viewInstance).ToView().AsPermanent(),
                "A second, independent registration of the same concrete type under the other " +
                "domain must be rejected - a type's registration identity spans both domains.");
            Assert.That(ex.Message, Does.Contain("ThingA"));
        }

        [Test]
        public void Register_DifferentInstanceToOppositeDomain_Interface_Throws()
        {
            var logicInstance = new ThingA();
            _container.Register(logicInstance).ToLogicAs<IThing>().AsPermanent();

            var viewInstance = new ThingB();
            var ex = Assert.Throws<InvalidOperationException>(
                () => _container.Register(viewInstance).ToViewAs<IThing>().AsPermanent(),
                "The same guard must apply to interface-keyed registrations, not just concrete types.");
            Assert.That(ex.Message, Does.Contain("IThing"));
        }

        [Test]
        public void Register_DifferentInstanceToOppositeDomain_RejectedAttempt_LeavesOriginalIntact()
        {
            var logicInstance = new ThingA();
            _container.Register(logicInstance).ToLogic().AsPermanent();

            var viewInstance = new ThingA();
            Assert.Throws<InvalidOperationException>(
                () => _container.Register(viewInstance).ToView().AsPermanent());

            Assert.That(_container.Resolve<ThingA>(), Is.SameAs(logicInstance),
                "The original, legitimately-committed Logic registration must be completely " +
                "unaffected by the rejected cross-domain attempt - the check runs in phase 1, " +
                "before any commit, so there is nothing to roll back.");
            Assert.Throws<InvalidOperationException>(() => _container.Unregister<ThingA>(),
                "The original registration must still correctly report itself as permanent.");
        }

        [Test]
        public void Register_SameInstanceBothDomainsInOneChain_StillSucceeds()
        {
            var instance = new ThingA();

            Assert.DoesNotThrow(
                () => _container.Register(instance).ToLogicAs<IThing>().ToViewAs<IThing>().AsPermanent(),
                "Exposing ONE instance to both domains via a single Register(...) chain must remain " +
                "fully supported - the new cross-domain check must never see this chain's own " +
                "not-yet-committed types as a conflict.");

            Assert.That(_container.Resolve<IThing>(), Is.SameAs(instance));
            using (_container.BeginViewScope())
            {
                Assert.That(_container.Resolve<IThing>(), Is.SameAs(instance));
            }
        }

        [Test]
        public void Register_SameTypeTwiceInSameDomain_StillThrowsExistingDuplicateError()
        {
            _container.Register(new ThingA()).ToLogic().AsPermanent();

            var ex = Assert.Throws<InvalidOperationException>(
                () => _container.Register(new ThingA()).ToLogic().AsPermanent(),
                "The pre-existing same-domain duplicate guard must be unaffected by this fix.");
            Assert.That(ex.Message, Does.Contain("already registered in logic container"));
        }
    }
}
