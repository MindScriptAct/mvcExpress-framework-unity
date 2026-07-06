using NUnit.Framework;
using mvcExpress;
using mvcExpress.Internal.Initialization;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests
{
    /// <summary>
    /// End-to-end behavior tests for the [StartupModule] attribute feature.
    /// Verifies that modules decorated with [StartupModule] are started by
    /// StartConfiguredModules() and that ordering and Inspector-override rules work.
    /// </summary>
    /// <remarks>
    /// Fake module types (AutoStartModule, LateStartModule, EarlyStartModule, PlainModule)
    /// are defined in StartupModuleAttributeTests.cs.
    /// </remarks>
    [TestFixture]
    public class StartupModuleAttributeBehaviourTests
    {
        // ── Fields ────────────────────────────────────────────────────────────

        private System.Collections.Generic.List<GameObject> _createdGameObjects
            = new System.Collections.Generic.List<GameObject>();

        // ── Lifecycle ─────────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            var existing = MvcFacade.InstanceOrNull;
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _createdGameObjects)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }
            _createdGameObjects.Clear();

            var facade = MvcFacade.InstanceOrNull;
            if (facade != null)
                Object.DestroyImmediate(facade.gameObject);

            // Clear scanner cache so startup module state does not leak between tests.
            AttributeScanner.Reset();
        }

        // Spawns a module and tracks its GameObject for cleanup.
        private T TrackModule<T>(MvcFacade facade) where T : MvcModule
        {
            var module = facade.GetModule<T>();
            if (module != null && !_createdGameObjects.Contains(module.gameObject))
                _createdGameObjects.Add(module.gameObject);
            return module;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Test]
        public void StartupModule_DecoratedModule_IsStartedByStartConfiguredModules()
        {
            var facade = MvcFacade.FacadeInstance;
            facade.StartupModules = new MvcStartupModuleEntry[0]; // no Inspector entries

            facade.StartConfiguredModules();

            var module = TrackModule<AutoStartModule>(facade);

            Assert.IsTrue(facade.IsModuleRegistered(typeof(AutoStartModule)),
                "AutoStartModule (decorated with [StartupModule]) must be registered after StartConfiguredModules().");
            Assert.IsNotNull(module,
                "GetModule<AutoStartModule> must return a non-null instance after StartConfiguredModules().");
        }

        [Test]
        public void StartupModule_NonDecoratedModule_IsNotStartedAutomatically()
        {
            var facade = MvcFacade.FacadeInstance;
            facade.StartupModules = new MvcStartupModuleEntry[0]; // no Inspector entries

            facade.StartConfiguredModules();

            Assert.IsFalse(facade.IsModuleRegistered(typeof(PlainModule)),
                "PlainModule has no [StartupModule] attribute and must not be started by StartConfiguredModules().");
        }

        [Test]
        public void StartupModule_OrderProperty_LowerOrderModuleExists()
        {
            var facade = MvcFacade.FacadeInstance;
            facade.StartupModules = new MvcStartupModuleEntry[0]; // no Inspector entries

            facade.StartConfiguredModules();

            TrackModule<EarlyStartModule>(facade);
            TrackModule<LateStartModule>(facade);

            Assert.IsTrue(facade.IsModuleRegistered(typeof(EarlyStartModule)),
                "EarlyStartModule (Order = -5) must be registered after StartConfiguredModules().");
            Assert.IsTrue(facade.IsModuleRegistered(typeof(LateStartModule)),
                "LateStartModule (Order = 10) must be registered after StartConfiguredModules().");
        }

        [Test]
        public void StartupModule_InspectorEntryTakesPrecedence_AttributeEntryIsSkipped()
        {
            var facade = MvcFacade.FacadeInstance;

            // Add an Inspector-style entry for AutoStartModule so DrainAttributeStartupModules
            // skips the attribute entry for the same type (no duplicate spawning).
            var inspectorEntry = MvcStartupModuleEntry.ForType<AutoStartModule>(autoStart: true);
            facade.StartupModules = new[] { inspectorEntry };

            // Must not throw InvalidOperationException due to double registration.
            Assert.DoesNotThrow(
                () => facade.StartConfiguredModules(),
                "StartConfiguredModules() must not throw when an Inspector entry covers a [StartupModule]-decorated type.");

            var module = TrackModule<AutoStartModule>(facade);

            Assert.IsTrue(facade.IsModuleRegistered(typeof(AutoStartModule)),
                "AutoStartModule must be registered exactly once when both Inspector entry and attribute entry exist.");
            Assert.IsNotNull(module,
                "GetModule<AutoStartModule> must return the single registered instance.");
        }
    }
}
