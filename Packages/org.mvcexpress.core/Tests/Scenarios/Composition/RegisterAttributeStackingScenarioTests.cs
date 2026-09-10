using System.Collections;
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

        [UnityTest]
        public IEnumerator TwoRegisterAttributes_SameModule_SameInterface_ThrowsInsteadOfSilentlySkipping()
        {
            // The duplicate-in-chain collision is caught by RegisterAttributeProxyGroup's own
            // registration try/catch, logged via MvcDebug.LogError, and recorded as a composition
            // failure. Initialization still runs to completion (phases 2-5 all get a chance to run),
            // but the recorded failure is then thrown as one aggregated MvcCompositionException
            // instead of being silently tolerated - same fatal path as
            // TwoRegisterAttributes_SameClass_ConflictingLifecycle_Throws below.
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("already mapped to logic scope"));
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("failed to initialize and will be destroyed"));
            LogAssert.Expect(LogType.Exception,
                new System.Text.RegularExpressions.Regex("attribute composition error"));

            _moduleGo = new GameObject(nameof(DuplicateStackingModule));
            try
            {
                _moduleGo.AddComponent<DuplicateStackingModule>();
            }
            catch (System.Exception)
            {
                // AddComponent may or may not surface the exception depending on Unity version;
                // either way the module has already self-destroyed by the time this returns.
            }

            // Destroy(gameObject) is deferred to end-of-frame in Play Mode - yield once so it
            // actually takes effect before checking Unity's overloaded null comparison.
            yield return null;

            Assert.IsTrue(_moduleGo == null,
                "A module with an unresolved duplicate [Register] mapping must be destroyed, not " +
                "left running with the conflict silently unresolved.");
        }

        [Register(typeof(LifecycleConflictModule), RegisterToLogic = true, LogicInterface = typeof(IReadOnlyStackedThing), Lifecycle = RegistrationLifecycle.Permanent)]
        [Register(typeof(LifecycleConflictModule), RegisterToLogic = true, LogicInterface = typeof(IMeasurementStackedThing), Lifecycle = RegistrationLifecycle.Transient)]
        public sealed class LifecycleConflictProxy : Proxy, IReadOnlyStackedThing, IMeasurementStackedThing { }

        public sealed class LifecycleConflictModule : MvcModule { }

        [UnityTest]
        public IEnumerator TwoRegisterAttributes_SameClass_ConflictingLifecycle_Throws()
        {
            // ResolveGroupLifecycle throws (uncaught) from inside RegisterAttributeProxyGroup,
            // propagating out through Awake(). Unity logs this as an unhandled exception, which
            // Unity's test runner auto-fails on unless declared expected via LogAssert.Expect.
            // Whether AddComponent itself re-throws to the caller is unreliable across Unity
            // versions (see ModuleInitializerTests.cs's ErrorHandlingAndDeferredLogging_AbortsOnFailure
            // for the same established pattern in this codebase), so we tolerate either outcome.
            //
            // A module that throws during initialization self-destroys immediately
            // (MvcModule.Awake()), which disposes and nulls its DI container as part of the normal
            // OnDestroy() teardown - so "query the container after the fact" is no longer a
            // meaningful ground truth (there is no container left to query, let alone a partial
            // one). The module's own destruction is the strictly stronger guarantee this test's
            // original comment wanted: if the whole module is gone, neither interface can possibly
            // resolve, not just these two specifically.
            //
            // LogAssert.Expect enforces chronological order: MvcModule's own error log fires
            // (synchronously, before self-destroy) BEFORE the rethrown exception reaches Unity's
            // engine-level log, so it must be declared first here.
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("failed to initialize and will be destroyed"));
            LogAssert.Expect(LogType.Exception,
                new System.Text.RegularExpressions.Regex("conflicting Lifecycle values"));

            _moduleGo = new GameObject(nameof(LifecycleConflictModule));
            try
            {
                _moduleGo.AddComponent<LifecycleConflictModule>();
            }
            catch (System.Exception)
            {
                // AddComponent may or may not surface the exception depending on Unity version;
                // either way the module has already self-destroyed by the time this returns.
            }

            // Destroy(gameObject) is deferred to end-of-frame in Play Mode - yield once so it
            // actually takes effect before checking Unity's overloaded null comparison.
            yield return null;

            Assert.IsTrue(_moduleGo == null,
                "A module whose initialization throws must be destroyed.");
        }
    }
}
