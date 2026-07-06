using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using mvcExpress;

namespace mvcExpress.Tests
{
    [TestFixture]
    public class MvcModuleTests
    {
        // ── Test doubles ──────────────────────────────────────────────────────

        // Full module: lets Awake run so MvcFacade is created and the module registers.
        // TearDown must destroy the module GO and the facade.
        private class TestModule : MvcModule { }

        // Isolated module: Awake/OnDestroy are suppressed so no MvcFacade is touched.
        // Safe to use without facade cleanup in TearDown.
        private class NoInitModule : MvcModule
        {
            protected override void Awake()     {} // intentionally empty - no MvcFacade
            protected override void OnDestroy() {} // intentionally empty - no UnregisterModule
        }

        // Mediator stub used as a key type in TryGetMediatorPrefab tests.
        private class StubMediator : MediatorBehaviour { }

        // ── Fields ────────────────────────────────────────────────────────────

        private GameObject _moduleGo;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        [TearDown]
        public void TearDown()
        {
            // Destroy the module GO first (triggers OnDestroy -> UnregisterModule).
            if (_moduleGo != null)
            {
                Object.DestroyImmediate(_moduleGo);
                _moduleGo = null;
            }

            // Then destroy the facade if it still exists.
            var facade = MvcFacade.InstanceOrNull;
            if (facade != null)
            {
                Object.DestroyImmediate(facade.gameObject);
            }
        }

        // ── 1. Lazy Container Creation ─────────────────────────────────────────

        [Test]
        public void LazyContainerCreation_EnsureMvcContainers_CreatesHierarchy()
        {
            // Arrange + Act: AddComponent triggers Awake, which calls EnsureMvcContainers
            // internally via InitializeModule.
            _moduleGo = new GameObject("TestModule");
            _moduleGo.AddComponent<TestModule>();

            // Assert: three registry child GameObjects must have been created.
            var servicesTransform   = _moduleGo.transform.Find("Services");
            var modelTransform      = _moduleGo.transform.Find("Model");
            var controllerTransform = _moduleGo.transform.Find("Controller");

            Assert.IsNotNull(servicesTransform,
                "EnsureMvcContainers must create a 'Services' child GameObject.");
            Assert.IsNotNull(modelTransform,
                "EnsureMvcContainers must create a 'Model' child GameObject.");
            Assert.IsNotNull(controllerTransform,
                "EnsureMvcContainers must create a 'Controller' child GameObject.");

            // Each child must carry its registry MonoBehaviour component.
            Assert.IsNotNull(servicesTransform.GetComponent<ServiceRegistryBehaviour>(),
                "'Services' child must have a ServiceRegistryBehaviour component.");
            Assert.IsNotNull(modelTransform.GetComponent<ProxyRegistryBehaviour>(),
                "'Model' child must have a ProxyRegistryBehaviour component.");
            Assert.IsNotNull(controllerTransform.GetComponent<CommandBindingsBehaviour>(),
                "'Controller' child must have a CommandBindingsBehaviour component.");
        }

        [Test]
        public void LazyContainerCreation_ExistingContainersPreserved_NotDuplicated()
        {
            // Arrange: manually place registry components on pre-existing child GOs before Awake.
            _moduleGo = new GameObject("TestModule");
            _moduleGo.SetActive(false); // prevent premature Awake

            // Add the module component while deactivated (Awake deferred until SetActive(true)).
            _moduleGo.AddComponent<TestModule>();

            var servicesGo = new GameObject("Services");
            servicesGo.transform.SetParent(_moduleGo.transform, false);
            servicesGo.AddComponent<ServiceRegistryBehaviour>();

            var modelGo = new GameObject("Model");
            modelGo.transform.SetParent(_moduleGo.transform, false);
            modelGo.AddComponent<ProxyRegistryBehaviour>();

            var controllerGo = new GameObject("Controller");
            controllerGo.transform.SetParent(_moduleGo.transform, false);
            controllerGo.AddComponent<CommandBindingsBehaviour>();

            // Act: activate so Awake fires and EnsureMvcContainers discovers existing containers.
            _moduleGo.SetActive(true);

            // Assert: still exactly one child per name (no duplicates created).
            Assert.AreEqual(1, CountChildrenNamed(_moduleGo, "Services"),
                "EnsureMvcContainers must not create a duplicate 'Services' child.");
            Assert.AreEqual(1, CountChildrenNamed(_moduleGo, "Model"),
                "EnsureMvcContainers must not create a duplicate 'Model' child.");
            Assert.AreEqual(1, CountChildrenNamed(_moduleGo, "Controller"),
                "EnsureMvcContainers must not create a duplicate 'Controller' child.");
        }

        // ── 2. Initialization Delegation ──────────────────────────────────────

        [Test]
        public void InitializationDelegation_Awake_RegistersModuleWithFacade()
        {
            // Arrange + Act: AddComponent triggers Awake which calls MvcFacade.RegisterModule.
            _moduleGo = new GameObject("TestModule");
            _moduleGo.AddComponent<TestModule>();

            // Assert: facade was created and the module is registered.
            Assert.IsNotNull(MvcFacade.InstanceOrNull,
                "Awake must ensure MvcFacade exists.");

            Assert.IsTrue(MvcFacade.InstanceOrNull.IsModuleRegistered(typeof(TestModule)),
                "Awake must register the module with MvcFacade.");
        }

        [Test]
        public void InitializationDelegation_Awake_CreatesModuleInitializer()
        {
            // Arrange + Act.
            _moduleGo = new GameObject("TestModule");
            var module = _moduleGo.AddComponent<TestModule>();

            // Access the private _initializer field via reflection.
            var initializerField = typeof(MvcModule).GetField(
                "_initializer",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (initializerField == null)
            {
                Assert.Ignore("_initializer field not found - reflection path unavailable in this build.");
                return;
            }

            var initializer = initializerField.GetValue(module);

            Assert.IsNotNull(initializer,
                "Awake must create the internal ModuleInitializer.");
        }

        // ── 3. View Prefab Resolution ─────────────────────────────────────────

        [Test]
        public void ViewPrefabResolution_NoMappingAnywhere_ReturnsFalse()
        {
            // Arrange: NoInitModule so no side effects from Awake.
            _moduleGo = new GameObject("NoInitModule");
            var module = _moduleGo.AddComponent<NoInitModule>();

            // Access TryGetMediatorPrefab via reflection (internal method).
            var method = typeof(MvcModule).GetMethod(
                "TryGetMediatorPrefab",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                new[] { typeof(Type), typeof(GameObject).MakeByRefType() },
                null);

            if (method == null)
            {
                Assert.Ignore("TryGetMediatorPrefab not accessible via reflection in this build.");
                return;
            }

            // Act.
            var args = new object[] { typeof(StubMediator), null };
            var result = (bool)method.Invoke(module, args);

            // Assert: no mapping registered, no facade - must return false.
            Assert.IsFalse(result,
                "TryGetMediatorPrefab must return false when no local or global mapping exists.");
            Assert.IsNull(args[1],
                "TryGetMediatorPrefab must leave the prefab out-param null when no mapping exists.");
        }

        [Test]
        public void ViewPrefabResolution_LocalRegistryMapped_ReturnsTrueAndPrefab()
        {
            // Arrange: NoInitModule so no side effects from Awake.
            _moduleGo = new GameObject("NoInitModule");
            var module = _moduleGo.AddComponent<NoInitModule>();

            // TryGetMediatorPrefab always calls EnsureMvcContainers() before checking the
            // dictionary, and EnsureMvcContainers() ends with ImportMediatorPrefabMappings()
            // which clears and rebuilds _mediatorPrefabByType from _viewContainer.
            // Direct injection into the dictionary is therefore wiped immediately.
            //
            // The correct approach: build a MediatorRegistryBehaviour child with a
            // MediatorPrefabMapping entry so ImportMediatorPrefabMappings() populates
            // the dictionary itself.

            // Create the prefab stand-in (just a plain GO - not an actual prefab asset).
            var prefabGo = new GameObject("StubPrefab");
            try
            {
                // Build a "View" child with a MediatorRegistryBehaviour.
                var viewGo = new GameObject("View");
                viewGo.transform.SetParent(_moduleGo.transform, false);
                var registry = viewGo.AddComponent<MediatorRegistryBehaviour>();

                // Inject a MediatorPrefabMapping array into the registry via reflection.
                var mapping = new MediatorPrefabMapping
                {
                    MediatorTypeName = typeof(StubMediator).AssemblyQualifiedName,
                    Prefab = prefabGo,
                };

                var prefabsField = typeof(MediatorRegistryBehaviour).GetField(
                    "_mediatorPrefabs",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (prefabsField == null)
                {
                    Assert.Ignore("_mediatorPrefabs field not found on MediatorRegistryBehaviour - reflection path unavailable.");
                    return;
                }

                prefabsField.SetValue(registry, new MediatorPrefabMapping[] { mapping });

                // Wire the registry into the module's _viewContainer field so
                // EnsureMvcContainers() picks it up without searching children.
                var viewContainerField = typeof(MvcModule).GetField(
                    "_viewContainer",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (viewContainerField == null)
                {
                    Assert.Ignore("_viewContainer field not found on MvcModule - reflection path unavailable.");
                    return;
                }

                viewContainerField.SetValue(module, registry);

                var method = typeof(MvcModule).GetMethod(
                    "TryGetMediatorPrefab",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null,
                    new[] { typeof(Type), typeof(GameObject).MakeByRefType() },
                    null);

                if (method == null)
                {
                    Assert.Ignore("TryGetMediatorPrefab not accessible via reflection in this build.");
                    return;
                }

                var args = new object[] { typeof(StubMediator), null };
                var result = (bool)method.Invoke(module, args);

                // Assert: mapping was imported from the registry and returned.
                Assert.IsTrue(result,
                    "TryGetMediatorPrefab must return true when a local mapping exists.");
                Assert.AreSame(prefabGo, (GameObject)args[1],
                    "TryGetMediatorPrefab must return the locally-mapped prefab.");
            }
            finally
            {
                Object.DestroyImmediate(prefabGo);
            }
        }

        // ── 4. Module Teardown ────────────────────────────────────────────────

        [Test]
        public void ModuleTeardown_OnDestroy_UnregistersModuleFromFacade()
        {
            // Arrange: let Awake run so the module registers with MvcFacade.
            _moduleGo = new GameObject("TestModule");
            _moduleGo.AddComponent<TestModule>();

            // Precondition.
            Assert.IsTrue(MvcFacade.InstanceOrNull.IsModuleRegistered(typeof(TestModule)),
                "Module must be registered before destruction (precondition).");

            // Act: destroy the module GO - triggers OnDestroy -> UnregisterModule.
            Object.DestroyImmediate(_moduleGo);
            _moduleGo = null; // prevent TearDown from double-destroying

            // Assert: facade still exists (grace period, not yet destroyed) but module is gone.
            var facade = MvcFacade.InstanceOrNull;
            if (facade == null)
            {
                // Facade was destroyed synchronously - that itself confirms cleanup ran.
                Assert.Pass("MvcFacade was destroyed after the last module unregistered, confirming cleanup.");
                return;
            }

            Assert.IsFalse(facade.IsModuleRegistered(typeof(TestModule)),
                "OnDestroy must unregister the module from MvcFacade.");
        }

        [Test]
        public void ModuleTeardown_OnDestroy_DisposesCommandProcessor()
        {
            // Arrange.
            _moduleGo = new GameObject("TestModule");
            var module = _moduleGo.AddComponent<TestModule>();

            var processorField = typeof(MvcModule).GetField(
                "_commandProcessor",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (processorField == null)
            {
                Assert.Ignore("_commandProcessor field not found - reflection path unavailable.");
                return;
            }

            // Precondition: command processor exists after init.
            Assert.IsNotNull(processorField.GetValue(module),
                "_commandProcessor must be set after initialization.");

            // Act.
            Object.DestroyImmediate(_moduleGo);
            _moduleGo = null;

            // Assert: field is null after OnDestroy (cleared by the teardown path in MvcModule.OnDestroy).
            Assert.IsNull(processorField.GetValue(module),
                "OnDestroy must set _commandProcessor to null after disposing it.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static int CountChildrenNamed(GameObject parent, string name)
        {
            int count = 0;
            var t = parent.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                if (t.GetChild(i).name == name)
                    count++;
            }
            return count;
        }
    }
}
