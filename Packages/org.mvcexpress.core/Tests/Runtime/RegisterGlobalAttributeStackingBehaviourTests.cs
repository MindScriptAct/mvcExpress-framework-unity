using NUnit.Framework;
using mvcExpress.Internal.Initialization;
using UnityEngine;

namespace mvcExpress.Tests
{
    public class RegisterGlobalAttributeStackingBehaviourTests
    {
        [SetUp]
        [TearDown]
        public void ResetFacade()
        {
            AttributeScanner.Reset();
            var facade = MvcFacade.InstanceOrNull;
            if (facade != null) UnityEngine.Object.DestroyImmediate(facade.gameObject);
        }

        [Test]
        public void StackedRegisterGlobal_DifferentInterfaces_ShareOneGlobalInstance()
        {
            _ = MvcFacade.FacadeInstance; // triggers InitializeIfNeeded -> scan + drain

            var asReadOnly = MvcFacade.Global.Resolve<IReadOnlyGlobalStackedThing>();
            var asMeasurement = MvcFacade.Global.Resolve<IMeasurementGlobalStackedThing>();

            Assert.That(asReadOnly, Is.SameAs(asMeasurement));
        }

        // "Same interface stacked twice throws" is already covered without any facade/scanning
        // involvement: MvcDiContainer_MultiMapping_UnitTests.cs proves the chain-level duplicate
        // check itself, and StackedRegisterGlobal_DifferentInterfaces_ShareOneGlobalInstance above
        // already proves the grouping/drain wiring calls multiple ToLogicAs(...) on one chain. A
        // dedicated negative fixture for this at the [RegisterGlobal] level would have to be a real,
        // permanently-scannable attributed class - which (unlike [Register]'s module-scoped
        // fixtures) would hard-throw during every OTHER test's facade boot for the rest of the test
        // run, not just its own. Not worth the collateral risk for coverage this is already proven by.

        // Lifecycle-conflict resolution is tested directly against AttributeGroupingUtility, not
        // through a real [RegisterGlobal]-decorated fixture - see the class-level comment on
        // GlobalLifecycleConflictProxy in RegisterGlobalAttributeTests.cs for why.
        [Test]
        public void ResolveGroupLifecycle_ConflictingLifecycleValues_ThrowsInvalidOperationException()
        {
            var lifecycles = new[] { RegistrationLifecycle.Permanent, RegistrationLifecycle.Transient };

            var ex = Assert.Throws<System.InvalidOperationException>(() =>
                AttributeGroupingUtility.ResolveGroupLifecycle(
                    typeof(GlobalLifecycleConflictProxy), lifecycles, "RegisterGlobal"));

            Assert.That(ex.Message, Does.Contain("conflicting Lifecycle values"));
        }
    }
}
