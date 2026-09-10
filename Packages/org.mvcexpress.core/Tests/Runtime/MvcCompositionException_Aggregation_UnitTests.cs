using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using mvcExpress.Internal.Initialization;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests
{
    // Regression coverage for CMP-02's central claim: initialization runs to completion and
    // reports every attribute-composition failure together in one MvcCompositionException, not
    // just the first one. Every other CMP-02 test (RegisterAttributeStackingScenarioTests,
    // MediatorFindInScene_HierarchyScope_ScenarioTests, RegisterGlobalAttributeBehaviourTests)
    // exercises exactly one failure - none of them would catch a regression that made
    // ThrowIfCompositionFailuresRecorded() throw on the first problem found instead of collecting
    // all of them first.
    //
    // Module-scoped only: [RegisterGlobal] has no TargetModuleType to scope it to one test's
    // module, so a throwing [RegisterGlobal] type would be picked up by every other test in the
    // assembly that touches MvcFacade.FacadeInstance (the attribute scan is assembly-wide and the
    // scanned types don't go away between tests) - that would break unrelated tests suite-wide, so
    // this class deliberately does not attempt a facade/global-scope equivalent.
    [TestFixture]
    [Category("Scenario")]
    public class MvcCompositionException_Aggregation_UnitTests
    {
        [Register(typeof(MultiFailureModule))]
        public sealed class ThrowingProxyA : Proxy
        {
            public ThrowingProxyA() => throw new InvalidOperationException("boom-A");
        }

        [Register(typeof(MultiFailureModule))]
        public sealed class ThrowingProxyB : Proxy
        {
            public ThrowingProxyB() => throw new InvalidOperationException("boom-B");
        }

        public sealed class MultiFailureModule : MvcModule { }

        private static ModuleInitializer GetInitializer(MvcModule module)
        {
            var field = typeof(MvcModule).GetField("_initializer", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Could not find '_initializer' field on MvcModule via reflection.");
            return (ModuleInitializer)field.GetValue(module);
        }

        [UnityTest]
        public IEnumerator Module_TwoIndependentProxyFailures_ThrowOneExceptionListingBoth()
        {
            LogAssert.Expect(LogType.Error, new Regex("boom-A"));
            LogAssert.Expect(LogType.Error, new Regex("boom-B"));
            LogAssert.Expect(LogType.Error, new Regex("failed to initialize and will be destroyed"));
            LogAssert.Expect(LogType.Exception, new Regex("2 attribute composition errors"));

            _moduleGo = new GameObject(nameof(MultiFailureModule));
            MultiFailureModule module = null;
            try
            {
                module = _moduleGo.AddComponent<MultiFailureModule>();
            }
            catch (Exception)
            {
                // AddComponent may or may not surface the exception depending on Unity version -
                // the component (and its _initializer field) is still inspectable either way, before
                // the deferred Destroy() below takes effect.
            }

            if (module == null)
                module = _moduleGo.GetComponent<MultiFailureModule>();

            var initializer = GetInitializer(module);
            var compositionException = initializer.FailureException as MvcCompositionException;

            Assert.IsNotNull(compositionException,
                "Two independent proxy construction failures must be reported as one " +
                "MvcCompositionException, not just the first one encountered.");
            Assert.AreEqual(2, compositionException.Failures.Count,
                "Both independent failures must be collected - a regression that throws on the " +
                "first failure instead of aggregating would leave this at 1.");
            Assert.IsTrue(compositionException.Failures.Any(f => f.Contains("boom-A")));
            Assert.IsTrue(compositionException.Failures.Any(f => f.Contains("boom-B")));

            yield return null;

            Assert.IsTrue(_moduleGo == null, "A module with recorded composition failures must be destroyed.");
        }

        // ── Lifecycle ────────────────────────────────────────────────────────

        private GameObject _moduleGo;

        [TearDown]
        public void TearDown()
        {
            if (_moduleGo != null)
            {
                Object.DestroyImmediate(_moduleGo);
                _moduleGo = null;
            }

            var facade = MvcFacade.InstanceOrNull;
            if (facade != null)
                Object.DestroyImmediate(facade.gameObject);
        }
    }
}
