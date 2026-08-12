using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace mvcExpress.Tests.Scenarios
{
    [TestFixture]
    [Category("Scenario")]
    public class RegisterAttributeStackingScenarioTests
    {
        public interface IReadOnlyStackedThing { }
        public interface IMeasurementStackedThing { }

        [Register(typeof(StackingModule), RegisterToLogic = true, LogicInterface = typeof(IReadOnlyStackedThing))]
        [Register(typeof(StackingModule), RegisterToLogic = true, LogicInterface = typeof(IMeasurementStackedThing))]
        public sealed class StackedInterfaceProxy : Proxy, IReadOnlyStackedThing, IMeasurementStackedThing { }

        [Register(typeof(DuplicateStackingModule), RegisterToLogic = true, LogicInterface = typeof(IReadOnlyStackedThing))]
        [Register(typeof(DuplicateStackingModule), RegisterToLogic = true, LogicInterface = typeof(IReadOnlyStackedThing))]
        public sealed class DuplicateInterfaceProxy : Proxy, IReadOnlyStackedThing { }

        public sealed class StackingModule : MvcModule { }
        public sealed class DuplicateStackingModule : MvcModule { }

        private GameObject _moduleGo;

        [TearDown]
        public void TearDown()
        {
            if (_moduleGo != null) { Object.DestroyImmediate(_moduleGo); _moduleGo = null; }
            var facade = MvcFacade.InstanceOrNull;
            if (facade != null) Object.DestroyImmediate(facade.gameObject);
        }

        [Test]
        public void TwoRegisterAttributes_SameModule_DifferentInterfaces_ShareOneInstance()
        {
            _moduleGo = new GameObject(nameof(StackingModule));
            var module = _moduleGo.AddComponent<StackingModule>();

            var asReadOnly = module.DiContainer.Resolve<IReadOnlyStackedThing>();
            var asMeasurement = module.DiContainer.Resolve<IMeasurementStackedThing>();

            Assert.That(asReadOnly, Is.Not.Null);
            Assert.That(asMeasurement, Is.Not.Null);
            Assert.That(asReadOnly, Is.SameAs(asMeasurement),
                "Two [Register] attributes on one class targeting the same module must share ONE instance, " +
                "not create two separate instances - each registered under only one interface.");
        }

        [Test]
        public void TwoRegisterAttributes_SameModule_SameInterface_ThrowsInsteadOfSilentlySkipping()
        {
            // The duplicate-in-chain collision is caught by RegisterAttributeProxyGroup's own
            // registration try/catch and logged via MvcDebug.LogError (not rethrown) - Unity's test
            // runner auto-fails a test on any unexpected error-level log, so it must be declared
            // expected. AddComponent itself does not throw for this path (the error is caught
            // internally), so no Assert.Throws is needed around it - only around the ground-truth
            // Resolve() check below, which is what actually proves the duplicate didn't silently win.
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("already mapped to logic scope"));

            _moduleGo = new GameObject(nameof(DuplicateStackingModule));
            var module = _moduleGo.AddComponent<DuplicateStackingModule>();

            // Force the error to surface even if the framework logs-and-continues at a lower
            // layer: assert the type never became resolvable in a way that hides the conflict.
            Assert.Throws<System.InvalidOperationException>(
                () => module.DiContainer.Resolve<IReadOnlyStackedThing>());
        }

        [Register(typeof(LifecycleConflictModule), RegisterToLogic = true, LogicInterface = typeof(IReadOnlyStackedThing), Lifecycle = RegistrationLifecycle.Permanent)]
        [Register(typeof(LifecycleConflictModule), RegisterToLogic = true, LogicInterface = typeof(IMeasurementStackedThing), Lifecycle = RegistrationLifecycle.Transient)]
        public sealed class LifecycleConflictProxy : Proxy, IReadOnlyStackedThing, IMeasurementStackedThing { }

        public sealed class LifecycleConflictModule : MvcModule { }

        [Test]
        public void TwoRegisterAttributes_SameClass_ConflictingLifecycle_Throws()
        {
            // ResolveGroupLifecycle throws (uncaught) from inside RegisterAttributeProxyGroup,
            // propagating out through Awake(). Unity logs this as an unhandled exception, which
            // Unity's test runner auto-fails on unless declared expected via LogAssert.Expect.
            // Whether AddComponent itself re-throws to the caller is unreliable across Unity
            // versions (see ModuleInitializerTests.cs's ErrorHandlingAndDeferredLogging_AbortsOnFailure
            // for the same established pattern in this codebase), so we tolerate either outcome and
            // verify ground truth via container state instead of wrapping AddComponent in Assert.Throws.
            LogAssert.Expect(LogType.Exception,
                new System.Text.RegularExpressions.Regex("conflicting Lifecycle values"));

            _moduleGo = new GameObject(nameof(LifecycleConflictModule));
            LifecycleConflictModule module = null;
            try
            {
                module = _moduleGo.AddComponent<LifecycleConflictModule>();
            }
            catch (System.Exception)
            {
                // AddComponent may or may not surface the exception depending on Unity version;
                // either way the component exists and Awake ran up to the failure point.
            }

            if (module == null)
                module = _moduleGo.GetComponent<LifecycleConflictModule>();

            // Ground truth: the lifecycle conflict must abort registration for the whole group -
            // neither interface should resolve, since the group's registration never completed.
            Assert.Throws<System.InvalidOperationException>(
                () => module.DiContainer.Resolve<IReadOnlyStackedThing>());
            Assert.Throws<System.InvalidOperationException>(
                () => module.DiContainer.Resolve<IMeasurementStackedThing>());
        }
    }
}
