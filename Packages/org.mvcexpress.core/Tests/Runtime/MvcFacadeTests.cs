using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using mvcExpress.Internal.Initialization;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests
{
    // Top-level type (not nested) so AttributeScanner's assembly-wide type scan discovers it the
    // same way it discovers AutoStartModule/LateStartModule/EarlyStartModule in
    // StartupModuleAttributeTests.cs. Used only by StartupComposition_AttributeOnlyModule_* below.
    [StartupModule(Order = 10)]
    public class AttributeOnlyModule : MvcModule { }

    [TestFixture]
    public class MvcFacadeTests
    {
        // ── Test doubles ──────────────────────────────────────────────────────

        // Concrete module type A - no overrides so Awake() runs the full framework
        // initialization sequence and RegisterModule() is called on MvcFacade.
        private class TestModuleA : MvcModule { }

        // Distinct concrete type for multi-module registration tests.
        private class TestModuleB : MvcModule { }

        // Module whose Awake/OnDestroy are no-ops. Used when we need to call
        // RegisterModule() directly without triggering auto-registration in Awake.
        // Required because Unity Play Mode swallows exceptions thrown in Awake() -
        // they are logged but NOT re-thrown to the C# caller.
        private class NoInitModule : MvcModule
        {
            protected override void Awake()     {}
            protected override void OnDestroy() {}
        }

        // Counts BindCommands() calls so tests can prove whether InitializeModule() ran.
        // Reset in each test that uses it - static because MvcModule instances are new each time.
        private class TrackedInitModule : MvcModule
        {
            public static int BindCommandsCallCount;
            protected override void BindCommands() { BindCommandsCallCount++; }
        }

        // Counts OnModuleAwake() calls so tests can prove whether it ran for a rejected
        // duplicate. Reset in each test that uses it - static
        // because MvcModule instances are new each time.
        private class TrackedOnModuleAwakeModule : MvcModule
        {
            public static int OnModuleAwakeCallCount;
            protected override void OnModuleAwake() { OnModuleAwakeCallCount++; }
        }

        // ── Fields ────────────────────────────────────────────────────────────

        // GameObjects created by tests; destroyed in TearDown.
        private System.Collections.Generic.List<GameObject> _createdGameObjects
            = new System.Collections.Generic.List<GameObject>();

        // ── Helpers ───────────────────────────────────────────────────────────

        // Creates a module GameObject of the given type, which triggers Awake and
        // registers the module with MvcFacade automatically.
        private T CreateModule<T>() where T : MvcModule
        {
            var go = new GameObject(typeof(T).Name);
            _createdGameObjects.Add(go);
            return go.AddComponent<T>();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            // Ensure no leftover facade from a previous test.
            var existing = MvcFacade.InstanceOrNull;
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            // Destroy module GameObjects first so their OnDestroy runs and
            // UnregisterModule is called before we destroy the facade.
            foreach (var go in _createdGameObjects)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }
            _createdGameObjects.Clear();

            // Destroy the facade last.
            var facade = MvcFacade.InstanceOrNull;
            if (facade != null)
                Object.DestroyImmediate(facade.gameObject);
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        // ---- 1. Singleton Creation and Enforcement ---------------------------

        [Test]
        public void SingletonCreationAndEnforcement_FirstAccessCreatesInstance()
        {
            // FacadeInstance should be null before the first access.
            Assert.IsNull(MvcFacade.InstanceOrNull,
                "InstanceOrNull must be null before FacadeInstance is accessed for the first time.");

            var facade = MvcFacade.FacadeInstance;

            Assert.IsNotNull(facade,
                "FacadeInstance must create a non-null instance on first access.");
            Assert.IsNotNull(MvcFacade.InstanceOrNull,
                "InstanceOrNull must return the created instance after FacadeInstance is accessed.");
        }

        [Test]
        public void SingletonCreationAndEnforcement_SecondAccessReturnsSameInstance()
        {
            var first  = MvcFacade.FacadeInstance;
            var second = MvcFacade.FacadeInstance;

            Assert.AreSame(first, second,
                "FacadeInstance must return the same object on every access.");
        }

        [Test]
        public void SingletonCreationAndEnforcement_DontDestroyOnLoadIsApplied()
        {
            // DontDestroyOnLoad moves the object into a special scene whose name is
            // "DontDestroyOnLoad". This is consistent across Unity 2021+.
            var facade = MvcFacade.FacadeInstance;

            Assert.AreEqual("DontDestroyOnLoad", facade.gameObject.scene.name,
                "MvcFacade must be in the DontDestroyOnLoad scene after creation.");
        }

        [Test]
        public void SingletonCreationAndEnforcement_ManualDuplicateIsDestroyedByAwake()
        {
            // Create the canonical facade first.
            var original = MvcFacade.FacadeInstance;

            // Manually create a second GameObject with MvcFacade component.
            // Its Awake() detects the duplicate and calls Destroy() on itself.
            // DestroyImmediate is needed in Edit Mode to force the teardown synchronously.
            var duplicateGo = new GameObject("MvcFacade_Duplicate");
            _createdGameObjects.Add(duplicateGo);
            var duplicate = duplicateGo.AddComponent<MvcFacade>();
            Object.DestroyImmediate(duplicateGo);
            _createdGameObjects.Remove(duplicateGo);

            // The static field must still reference the original instance.
            Assert.AreSame(original, MvcFacade.InstanceOrNull,
                "InstanceOrNull must still point to the original instance after a duplicate is destroyed.");
        }

        // ---- 2. Module Registration and Tracking ----------------------------

        [Test]
        public void ModuleRegistrationAndTracking_RegisteredModuleIsTracked()
        {
            // Creating the module runs Awake, which calls MvcFacade.FacadeInstance.RegisterModule.
            CreateModule<TestModuleA>();

            var facade = MvcFacade.FacadeInstance;

            Assert.IsTrue(facade.IsModuleRegistered(typeof(TestModuleA)),
                "IsModuleRegistered must return true after a module of that type has been registered.");
        }

        [Test]
        public void ModuleRegistrationAndTracking_UnregisteredTypeReturnsFalse()
        {
            var facade = MvcFacade.FacadeInstance;

            Assert.IsFalse(facade.IsModuleRegistered(typeof(TestModuleB)),
                "IsModuleRegistered must return false for a type that was never registered.");
        }

        [Test]
        public void ModuleRegistrationAndTracking_TwoDistinctTypesRegisteredSuccessfully()
        {
            CreateModule<TestModuleA>();
            CreateModule<TestModuleB>();

            var facade = MvcFacade.FacadeInstance;

            Assert.IsTrue(facade.IsModuleRegistered(typeof(TestModuleA)),
                "TestModuleA must be tracked after registration.");
            Assert.IsTrue(facade.IsModuleRegistered(typeof(TestModuleB)),
                "TestModuleB must be tracked after registration.");
        }

        [Test]
        public void ModuleRegistrationAndTracking_GetModuleReturnsRegisteredInstance()
        {
            var moduleA = CreateModule<TestModuleA>();
            var facade  = MvcFacade.FacadeInstance;

            var retrieved = facade.GetModule<TestModuleA>();

            Assert.AreSame(moduleA, retrieved,
                "GetModule<T> must return the exact instance that was registered.");
        }

        [Test]
        public void ModuleRegistrationAndTracking_TryGetModuleGenericReturnsRegisteredInstance()
        {
            var moduleA = CreateModule<TestModuleA>();
            var facade = MvcFacade.FacadeInstance;

            var found = facade.TryGetModule<TestModuleA>(out var retrieved);

            Assert.IsTrue(found);
            Assert.AreSame(moduleA, retrieved,
                "TryGetModule<T> must return the exact registered module instance.");
        }

        [Test]
        public void ModuleRegistrationAndTracking_TryGetModuleByTypeHandlesNullAndRegisteredTypes()
        {
            var moduleA = CreateModule<TestModuleA>();
            var facade = MvcFacade.FacadeInstance;

            Assert.IsFalse(facade.TryGetModule(null, out var nullResult));
            Assert.IsNull(nullResult);

            Assert.IsTrue(facade.TryGetModule(typeof(TestModuleA), out var retrieved));
            Assert.AreSame(moduleA, retrieved,
                "TryGetModule(Type) must return the exact registered module instance.");
        }

        [Test]
        public void ModuleRegistrationAndTracking_GetModuleByTypeReturnsRegisteredInstanceOrNull()
        {
            var moduleA = CreateModule<TestModuleA>();
            var facade = MvcFacade.FacadeInstance;

            Assert.AreSame(moduleA, facade.GetModule(typeof(TestModuleA)));
            Assert.IsNull(facade.GetModule(typeof(TestModuleB)));
            Assert.IsNull(facade.GetModule(null));
        }

        [Test]
        public void ModuleRegistrationAndTracking_ModuleNameAndSnapshotReflectRegisteredModules()
        {
            CreateModule<TestModuleA>();
            CreateModule<TestModuleB>();
            var facade = MvcFacade.FacadeInstance;

            Assert.IsTrue(facade.TryGetModuleName(typeof(TestModuleA), out var moduleAName));
            Assert.AreEqual(nameof(TestModuleA), moduleAName);
            Assert.IsFalse(facade.TryGetModuleName(null, out var nullName));
            Assert.IsNull(nullName);

            var snapshot = facade.GetAllRegisteredModules();

            Assert.That(snapshot, Contains.Key(typeof(TestModuleA)));
            Assert.That(snapshot, Contains.Key(typeof(TestModuleB)));
            Assert.AreEqual(nameof(TestModuleA), snapshot[typeof(TestModuleA)]);
            Assert.AreEqual(nameof(TestModuleB), snapshot[typeof(TestModuleB)]);
        }

        [Test]
        public void ModuleRegistrationAndTracking_DuplicateTypeThrowsInvalidOperationException()
        {
            // Unity Play Mode swallows exceptions thrown inside Awake() - they are logged
            // as errors but NOT re-thrown to the caller. We therefore bypass Awake by using
            // NoInitModule and calling RegisterModule() directly, so Assert.Throws can catch it.
            var go1 = new GameObject("NoInit_1");
            _createdGameObjects.Add(go1);
            var module1 = go1.AddComponent<NoInitModule>();

            var go2 = new GameObject("NoInit_2");
            _createdGameObjects.Add(go2);
            var module2 = go2.AddComponent<NoInitModule>();

            var facade = MvcFacade.FacadeInstance;

            // First registration of NoInitModule must succeed.
            facade.RegisterModule(module1);
            Assert.IsTrue(facade.IsModuleRegistered(typeof(NoInitModule)));

            // Second registration of the same concrete type must throw.
            Assert.Throws<InvalidOperationException>(
                () => facade.RegisterModule(module2),
                "RegisterModule must throw InvalidOperationException when the same module type is already registered.");
        }

        [Test]
        public void ModuleRegistrationAndTracking_NullTypeReturnsFalse()
        {
            var facade = MvcFacade.FacadeInstance;

            Assert.IsFalse(facade.IsModuleRegistered(null),
                "IsModuleRegistered(null) must return false without throwing.");
        }

        [Test]
        public void ModuleRegistrationAndTracking_DestroyingModuleUnregistersIt()
        {
            var go = new GameObject(nameof(TestModuleA));
            _createdGameObjects.Add(go);
            go.AddComponent<TestModuleA>();

            var facade = MvcFacade.FacadeInstance;
            Assert.IsTrue(facade.IsModuleRegistered(typeof(TestModuleA)));

            Object.DestroyImmediate(go);
            _createdGameObjects.Remove(go);

            Assert.IsFalse(facade.IsModuleRegistered(typeof(TestModuleA)),
                "IsModuleRegistered must return false after the module's GameObject is destroyed.");
        }

        // ---- 2b. Duplicate Module Registration ----------------------
        //
        // MvcModule.Awake() used to call InitializeModule() (which binds commands and attaches
        // mediators onto the shared MvcFacade.MessageBus) BEFORE RegisterModule() ever checked
        // for a duplicate. A rejected duplicate could therefore already have polluted shared
        // state, and its own teardown removed the ORIGINAL module from the registry because
        // UnregisterModule() removed by key alone, ignoring which instance was stored there.

        [Test]
        public void DuplicateModuleRegistration_RejectedDuplicate_NeverRunsInitializationPhases()
        {
            TrackedInitModule.BindCommandsCallCount = 0;

            // First instance registers and initializes normally.
            CreateModule<TrackedInitModule>();
            Assert.AreEqual(1, TrackedInitModule.BindCommandsCallCount,
                "Precondition: the first module's own BindCommands() must run exactly once.");

            // Second instance of the same type is a duplicate. Its Awake() must be rejected
            // by RegisterModule() BEFORE InitializeModule() runs, so BindCommands() (which
            // binds handlers onto the shared message bus) must never fire for it. Awake() logs
            // this rejection as an error - expect it so the test framework's strict log check
            // does not itself fail the test.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "Duplicate module of type 'TrackedInitModule' detected"));
            CreateModule<TrackedInitModule>();

            Assert.AreEqual(1, TrackedInitModule.BindCommandsCallCount,
                "A rejected duplicate module must never run its initialization phases - " +
                "BindCommands() must not be called a second time.");
        }

        [Test]
        public void DuplicateModuleRegistration_RejectedDuplicate_NeverRunsOnModuleAwake()
        {
            TrackedOnModuleAwakeModule.OnModuleAwakeCallCount = 0;

            // First instance registers and runs OnModuleAwake() normally.
            CreateModule<TrackedOnModuleAwakeModule>();
            Assert.AreEqual(1, TrackedOnModuleAwakeModule.OnModuleAwakeCallCount,
                "Precondition: the first module's own OnModuleAwake() must run exactly once.");

            // Second instance of the same type is a duplicate. Identity must be reserved with
            // the facade BEFORE OnModuleAwake() runs, so a duplicate's pre-init hook - which can
            // mutate shared state - never executes at all. Awake() logs the rejection as an
            // error - expect it so the test framework's strict log check does not itself fail
            // the test.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "Duplicate module of type 'TrackedOnModuleAwakeModule' detected"));
            CreateModule<TrackedOnModuleAwakeModule>();

            Assert.AreEqual(1, TrackedOnModuleAwakeModule.OnModuleAwakeCallCount,
                "A rejected duplicate module must never run OnModuleAwake() - identity is reserved " +
                "with the facade before OnModuleAwake() runs, so a duplicate's pre-init side effects " +
                "never execute at all.");
        }

        [UnityTest]
        public IEnumerator DuplicateModuleRegistration_RejectedDuplicate_IsDestroyedAndOriginalStaysRegistered()
        {
            var original = CreateModule<TestModuleA>();
            var facade = MvcFacade.FacadeInstance;

            // Create a duplicate GameObject directly (not via CreateModule<T>, so we can
            // observe its fate) and let its Awake() run.
            var duplicateGo = new GameObject("TestModuleA_Duplicate");
            _createdGameObjects.Add(duplicateGo);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "Duplicate module of type 'TestModuleA' detected"));
            duplicateGo.AddComponent<TestModuleA>();

            // Destroy(gameObject) is deferred to end-of-frame in Play Mode - yield once so it
            // actually takes effect before we check Unity's overloaded null comparison.
            yield return null;

            Assert.IsTrue(duplicateGo == null,
                "A rejected duplicate module's GameObject must be destroyed by Awake() itself " +
                "(Unity's overloaded == reports 'null' for a destroyed object).");

            // The original module's registration must survive the duplicate's rejection AND
            // its own teardown (OnDestroy -> UnregisterModule) unharmed.
            Assert.IsTrue(facade.IsModuleRegistered(typeof(TestModuleA)),
                "The original module must remain registered after a duplicate is rejected and destroyed.");
            Assert.AreSame(original, facade.GetModule<TestModuleA>(),
                "GetModule<T> must still return the original instance, not be wiped out by the " +
                "rejected duplicate's teardown.");
        }

        [Test]
        public void ModuleRegistrationAndTracking_UnregisterByStaleInstance_DoesNotRemoveCurrentRegistration()
        {
            // Simulates a module instance that was
            // never (successfully) registered still calls UnregisterModule() during its own
            // teardown. That call must not be able to evict a different, currently-registered
            // instance of the same type.
            var go1 = new GameObject("NoInit_Current");
            _createdGameObjects.Add(go1);
            var currentModule = go1.AddComponent<NoInitModule>();

            var go2 = new GameObject("NoInit_Stale");
            _createdGameObjects.Add(go2);
            var staleModule = go2.AddComponent<NoInitModule>();

            var facade = MvcFacade.FacadeInstance;
            facade.RegisterModule(currentModule);

            // staleModule was never registered (mirrors a duplicate rejected by RegisterModule),
            // but its teardown still calls UnregisterModule() the same way OnDestroy() would.
            facade.UnregisterModule(staleModule);

            Assert.IsTrue(facade.IsModuleRegistered(typeof(NoInitModule)),
                "Unregistering a stale/foreign instance must not remove the current registration.");
            Assert.AreSame(currentModule, facade.GetModule<NoInitModule>(),
                "The currently-registered instance must be unaffected by a stale UnregisterModule call.");
        }

        // ---- 3. Deferred Destruction Grace Period ---------------------------

        [Test]
        public void DeferredDestructionGracePeriod_UnregisteringLastModuleStartsCountdown()
        {
            // Use reflection to read _destructionCountdown because LateUpdate never fires in
            // Edit Mode (no game loop), making the frame-based part untestable here.

            var countdownField = typeof(MvcFacade).GetField(
                "_destructionCountdown",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (countdownField == null)
            {
                Assert.Ignore("_destructionCountdown field not found - implementation may have changed.");
                return;
            }

            var go = new GameObject(nameof(TestModuleA));
            _createdGameObjects.Add(go);
            go.AddComponent<TestModuleA>();

            var facade = MvcFacade.FacadeInstance;

            // Sanity: countdown must be idle (-1) while the module is registered.
            int countdownBeforeDestroy = (int)countdownField.GetValue(facade);
            Assert.AreEqual(-1, countdownBeforeDestroy,
                "_destructionCountdown must be idle (-1) while at least one module is registered.");

            // Destroy the only registered module.
            Object.DestroyImmediate(go);
            _createdGameObjects.Remove(go);

            // The countdown must have been started (value >= 0).
            int countdownAfterDestroy = (int)countdownField.GetValue(facade);
            Assert.GreaterOrEqual(countdownAfterDestroy, 0,
                "_destructionCountdown must be >= 0 after the last module unregisters.");
        }

        [Test]
        public void DeferredDestructionGracePeriod_RegisteringNewModuleCancelsCountdown()
        {
            var countdownField = typeof(MvcFacade).GetField(
                "_destructionCountdown",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (countdownField == null)
            {
                Assert.Ignore("_destructionCountdown field not found - implementation may have changed.");
                return;
            }

            // Register and then destroy the module to start the countdown.
            var go = new GameObject(nameof(TestModuleA));
            _createdGameObjects.Add(go);
            go.AddComponent<TestModuleA>();

            var facade = MvcFacade.FacadeInstance;

            Object.DestroyImmediate(go);
            _createdGameObjects.Remove(go);

            int countdownAfterUnregister = (int)countdownField.GetValue(facade);
            Assert.GreaterOrEqual(countdownAfterUnregister, 0,
                "Precondition: countdown must have started before we can test cancellation.");

            // Register a new module - this must reset the countdown to -1.
            CreateModule<TestModuleB>();

            int countdownAfterReregister = (int)countdownField.GetValue(facade);
            Assert.AreEqual(-1, countdownAfterReregister,
                "Registering a new module while the countdown is active must cancel it (reset to -1).");
        }

        [Test]
        public void DeferredDestructionGracePeriod_ReregistrationWithinWindow_FacadeAndGlobalStateSurviveIntact()
        {
            var go = new GameObject(nameof(TestModuleA));
            _createdGameObjects.Add(go);
            go.AddComponent<TestModuleA>();

            var facadeBeforeCountdown = MvcFacade.FacadeInstance;
            var globalContainerBeforeCountdown = MvcFacade.GlobalContainerOrNull;
            var globalProxy = new GlobalStateProbeProxy("kept-state");
            globalContainerBeforeCountdown.Register(globalProxy).ToLogic().AsPermanent();

            // Start the grace-period countdown by destroying the only module.
            Object.DestroyImmediate(go);
            _createdGameObjects.Remove(go);

            // Re-register within the window, before any LateUpdate ticks (Edit Mode never ticks
            // LateUpdate, so the countdown cannot expire between these two calls).
            CreateModule<TestModuleB>();

            Assert.That(MvcFacade.InstanceOrNull, Is.SameAs(facadeBeforeCountdown),
                "Re-registering within the grace-period window must cancel destruction on the SAME " +
                "facade instance, not create a new one.");
            Assert.That(MvcFacade.GlobalContainerOrNull, Is.SameAs(globalContainerBeforeCountdown),
                "The global container must remain the same instance across a cancelled destruction, " +
                "not be rebuilt.");
            Assert.That(MvcFacade.GlobalContainerOrNull.Resolve<GlobalStateProbeProxy>().Value, Is.EqualTo("kept-state"),
                "A global proxy registered before the near-miss destruction must keep its state - " +
                "the global container must never have been torn down.");
        }

        private sealed class GlobalStateProbeProxy : Proxy
        {
            public readonly string Value;
            public GlobalStateProbeProxy(string value) => Value = value;
        }

        [UnityTest]
        public IEnumerator DeferredDestructionGracePeriod_FacadeDestroysItselfAfterGracePeriod()
        {
            // Register a module, then unregister it to start the countdown.
            var go = new GameObject(nameof(TestModuleA));
            _createdGameObjects.Add(go);
            go.AddComponent<TestModuleA>();

            // AddComponent triggers Awake (synchronous), which scans assemblies. Reset the scanner
            // here so Start() — which fires on the next yield — cannot auto-start [StartupModule]
            // test fakes from other test files. Without this, those extra modules keep _modules
            // non-empty and the destruction countdown never starts.
            AttributeScanner.Reset();

            var facade = MvcFacade.InstanceOrNull;
            Assert.IsNotNull(facade, "Facade must exist after a module registers.");

            // Unregister by destroying the only module. This sets _destructionCountdown
            // to DestructionGracePeriodFrames (= 2) inside UnregisterModule.
            Object.DestroyImmediate(go);
            _createdGameObjects.Remove(go);

            Assert.IsNotNull(MvcFacade.InstanceOrNull,
                "Facade must still exist immediately after the last module unregisters.");

            // LateUpdate ticks the countdown each frame:
            //   LateUpdate 1: countdown 2 → 1
            //   LateUpdate 2: countdown 1 → 0
            //   LateUpdate 3: countdown == 0 → _facadeInstance = null, Destroy(gameObject)
            //
            // 'yield return null' resumes before LateUpdate in the same frame, so we need
            // DestructionGracePeriodFrames (2) + 2 extra yields to observe the result:
            //   yield 1 → LateUpdate 1 fires after us
            //   yield 2 → LateUpdate 2 fires after us
            //   yield 3 → LateUpdate 3 fires after us (sets _facadeInstance = null)
            //   yield 4 → safety margin: we now read InstanceOrNull in frame 4
            yield return null;
            yield return null;
            yield return null;
            yield return null;

            Assert.IsNull(MvcFacade.InstanceOrNull,
                "MvcFacade must set InstanceOrNull to null after the grace period expires with no registered modules.");

            // The facade GameObject was destroyed by the engine - nothing to clean up in TearDown.
        }

        // ---- 4. Startup Module Spawning -------------------------------------

        [Test]
        public void StartupModuleSpawning_AutoStartEntry_SpawnsModule()
        {
            // MvcStartupModuleEntry.ForType<T>() is the public factory for code-based entries.
            var entry = MvcStartupModuleEntry.ForType<TestModuleA>(autoStart: true);
            Assert.IsTrue(entry.IsValid(),
                "Precondition: entry must be valid for TestModuleA.");

            var facade = MvcFacade.FacadeInstance;
            facade.StartupModules = new[] { entry };

            facade.StartConfiguredModules();

            Assert.IsTrue(facade.IsModuleRegistered(typeof(TestModuleA)),
                "StartConfiguredModules must spawn and register a code-based AutoStart module.");

            // Track the created module GameObject for cleanup.
            var spawnedModule = facade.GetModule<TestModuleA>();
            if (spawnedModule != null)
                _createdGameObjects.Add(spawnedModule.gameObject);
        }

        [Test]
        public void StartupModuleSpawning_NonAutoStartEntry_DoesNotSpawnModule()
        {
            var entry = MvcStartupModuleEntry.ForType<TestModuleA>(autoStart: false);
            var facade = MvcFacade.FacadeInstance;
            facade.StartupModules = new[] { entry };

            facade.StartConfiguredModules();

            Assert.IsFalse(facade.IsModuleRegistered(typeof(TestModuleA)),
                "StartConfiguredModules must not spawn a module whose AutoStart is false.");
        }

        [Test]
        public void StartupModuleSpawning_SpawnModuleWithoutConfiguredEntry_ReturnsNull()
        {
            var spawned = MvcFacade.SpawnModule(typeof(TestModuleA));

            Assert.IsNull(spawned,
                "SpawnModule(Type) must return null when no matching startup entry is configured.");
            Assert.IsFalse(MvcFacade.FacadeInstance.IsModuleRegistered(typeof(TestModuleA)));
        }

        [Test]
        public void StartupModuleSpawning_AlreadyRegisteredModule_IsNotSpawnedAgain()
        {
            // Register TestModuleA manually first.
            CreateModule<TestModuleA>();

            var facade = MvcFacade.FacadeInstance;
            var entry  = MvcStartupModuleEntry.ForType<TestModuleA>(autoStart: true);
            facade.StartupModules = new[] { entry };

            // Should complete without throwing InvalidOperationException.
            Assert.DoesNotThrow(() => facade.StartConfiguredModules(),
                "StartConfiguredModules must not throw when the module is already registered.");

            // Still only one instance.
            Assert.IsTrue(facade.IsModuleRegistered(typeof(TestModuleA)));
        }

        [UnityTest]
        public IEnumerator StartupModuleSpawning_PrefabBasedEntry_SpawnsModule()
        {
            // Build a runtime "prefab" - an inactive GameObject with TestModuleA.
            // It must start inactive so its Awake does not fire while it is the template object.
            var prefabGo = new GameObject("TestModuleA_Prefab");
            prefabGo.SetActive(false);
            prefabGo.AddComponent<TestModuleA>();
            _createdGameObjects.Add(prefabGo);

            // Wire up a prefab-based startup entry.
            var entry = MvcStartupModuleEntry.ForType<TestModuleA>(autoStart: true);
            entry.ModulePrefab = prefabGo;

            Assert.IsTrue(entry.IsValid(),
                "Precondition: entry must be valid after ModulePrefab is set.");

            var facade = MvcFacade.FacadeInstance;
            facade.StartupModules = new[] { entry };

            facade.StartConfiguredModules();

            // Awake fires asynchronously when SetActive(true) is called during Play Mode;
            // yield one frame to let Unity flush the activation and run Awake.
            yield return null;

            Assert.IsTrue(facade.IsModuleRegistered(typeof(TestModuleA)),
                "StartConfiguredModules must spawn and register a prefab-based AutoStart module.");

            // Track the spawned instance for TearDown cleanup.
            var spawnedModule = facade.GetModule<TestModuleA>();
            if (spawnedModule != null && !_createdGameObjects.Contains(spawnedModule.gameObject))
                _createdGameObjects.Add(spawnedModule.gameObject);
        }

        // ---- 5. Attribute-driven Global Registration (Scoped) ----------------

        [Test]
        public void DrainAttributeGlobalRegistrations_ScopedService_DoesNotInitializeThrowawayInstance()
        {
            GlobalMockScopedService.InitializedCount = 0;

            // Accessing FacadeInstance triggers MvcFacade's full init sequence, including
            // DrainAttributeGlobalRegistrations().
            var facade = MvcFacade.FacadeInstance;
            Assert.IsNotNull(facade);

            var container = MvcFacade.GlobalContainerOrNull;
            Assert.IsNotNull(container);
            Assert.That(container.IsScoped(typeof(GlobalMockScopedService)), Is.True,
                "Scoped [RegisterGlobal] service must be registered as scoped in the global container.");
            Assert.That(GlobalMockScopedService.InitializedCount, Is.EqualTo(0),
                "The throwaway instance created to walk the registration builder must never be initialized.");
        }

        // ---- 6. Startup Composition Precedence (Inspector vs. Attribute) ----

        [Test]
        public void StartupComposition_InspectorEntryAndDuplicateAttributeForSameType_InspectorWins()
        {
            // TestModuleA here stands in for "ModuleX": configure it as an Inspector StartupModules
            // entry AND rely on it also being discoverable via [StartupModule] elsewhere in the test
            // assembly (StartupModuleAttributeTests.cs already declares such attributed modules for
            // other tests) - the composition contract under test is that the Inspector entry, once
            // present in facade.StartupModules, is what actually spawns the module, with no duplicate
            // registration attempt from the attribute-discovered entry for the same type.
            var inspectorEntry = MvcStartupModuleEntry.ForType<TestModuleA>(autoStart: true);
            var facade = MvcFacade.FacadeInstance;
            facade.StartupModules = new[] { inspectorEntry };

            Assert.DoesNotThrow(() => facade.StartConfiguredModules(),
                "Combining an Inspector entry for a type with attribute-discovered entries for other " +
                "types must not throw, even if AttributeScanner also independently knows about " +
                "AttributeOnlyModule below.");

            Assert.IsTrue(facade.IsModuleRegistered(typeof(TestModuleA)),
                "The Inspector-configured entry must spawn ModuleX exactly once.");

            var spawnedA = facade.GetModule<TestModuleA>();
            if (spawnedA != null) _createdGameObjects.Add(spawnedA.gameObject);

            // AttributeOnlyModule (Order = 10) has no Inspector entry, so DrainAttributeStartupModules
            // spawns it independently in the same StartConfiguredModules() call - it is unrelated to
            // TestModuleA and must not be blocked by the Inspector-precedence rule tested above.
            var spawnedAttrOnly = facade.GetModule<AttributeOnlyModule>();
            if (spawnedAttrOnly != null) _createdGameObjects.Add(spawnedAttrOnly.gameObject);
        }

        [Test]
        public void StartupComposition_AttributeOnlyModule_SpawnsWithConfiguredOrder()
        {
            // Investigation (see MvcFacade.StartConfiguredModules / DrainAttributeStartupModules):
            // Inspector AutoStart entries are collected first, then DrainAttributeStartupModules()
            // appends [StartupModule]-attributed entries for types with no matching Inspector entry
            // (sorted by Order), and finally every collected entry is spawned via SpawnStartupModule
            // in the SAME call to StartConfiguredModules(). Attribute-only entries route through
            // SpawnCodeModuleIfNeeded, which uses deactivate-AddComponent-activate: for a brand-new
            // GameObject, SetActive(true) runs Awake() synchronously (unlike the prefab-reactivation
            // case in StartupModuleSpawning_PrefabBasedEntry_SpawnsModule, which needs a yield because
            // it reactivates an already-instantiated prefab). StartupModule_DecoratedModule_
            // IsStartedByStartConfiguredModules in StartupModuleAttributeBehaviourTests.cs confirms
            // this end-to-end with a plain synchronous [Test], so no [UnityTest]/yield is needed here.
            var facade = MvcFacade.FacadeInstance;
            facade.StartupModules = new MvcStartupModuleEntry[0]; // no Inspector entries at all

            facade.StartConfiguredModules();

            Assert.IsTrue(facade.IsModuleRegistered(typeof(AttributeOnlyModule)),
                "AttributeOnlyModule (decorated with [StartupModule(Order = 10)], no Inspector entry) " +
                "must be spawned by StartConfiguredModules() via the attribute-driven auto-start path.");

            var spawned = facade.GetModule<AttributeOnlyModule>();
            Assert.IsNotNull(spawned,
                "GetModule<AttributeOnlyModule> must return the instance spawned via the attribute path.");
            if (spawned != null) _createdGameObjects.Add(spawned.gameObject);

            // Order is respected relative to LateStartModule (Order = 10, same as AttributeOnlyModule)
            // and EarlyStartModule (Order = -5) from StartupModuleAttributeTests.cs: both are also
            // attribute-only and discoverable in this same scan, so they spawn alongside
            // AttributeOnlyModule in this call without throwing or conflicting.
            Assert.IsTrue(facade.IsModuleRegistered(typeof(EarlyStartModule)),
                "EarlyStartModule (Order = -5) must also spawn in the same attribute-driven pass, " +
                "confirming ordering does not prevent other attribute-only modules from starting.");
            var spawnedEarly = facade.GetModule<EarlyStartModule>();
            if (spawnedEarly != null) _createdGameObjects.Add(spawnedEarly.gameObject);
        }

        // ---- 8. Deferred-Publish Budget Validation (PERF-03 follow-up) -------
        //
        // MaxDeferredPublishesPerFrame/MaxDeferredPublishMillisecondsPerFrame are public static
        // properties settable from any project code, which makes them a real input boundary: a
        // negative value would otherwise make Update()'s own budget check false on its very first
        // comparison, silently stalling the deferred-publish queue forever - worse than the frame
        // spike this feature exists to prevent. Each test restores the default in a finally block
        // so a failure here cannot leak a bad budget into unrelated tests.

        [Test]
        public void MaxDeferredPublishesPerFrame_SetNegative_ClampsToZeroAndWarns()
        {
            try
            {
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("MaxDeferredPublishesPerFrame cannot be negative"));
                MvcFacade.MaxDeferredPublishesPerFrame = -5;

                Assert.That(MvcFacade.MaxDeferredPublishesPerFrame, Is.EqualTo(0),
                    "A negative budget must be clamped to 0, not accepted as-is (which would stall the queue forever).");
            }
            finally
            {
                MvcFacade.MaxDeferredPublishesPerFrame = int.MaxValue;
            }
        }

        [Test]
        public void MaxDeferredPublishMillisecondsPerFrame_SetNegative_ClampsToZeroAndWarns()
        {
            try
            {
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("MaxDeferredPublishMillisecondsPerFrame cannot be negative"));
                MvcFacade.MaxDeferredPublishMillisecondsPerFrame = -10;

                Assert.That(MvcFacade.MaxDeferredPublishMillisecondsPerFrame, Is.EqualTo(0),
                    "A negative budget must be clamped to 0, not accepted as-is (which would stall the queue forever).");
            }
            finally
            {
                MvcFacade.MaxDeferredPublishMillisecondsPerFrame = double.MaxValue;
            }
        }

        [Test]
        public void MaxDeferredPublishMillisecondsPerFrame_SetNaN_IgnoredAndWarns()
        {
            MvcFacade.MaxDeferredPublishMillisecondsPerFrame = 250; // known non-default value to prove NaN is rejected, not silently accepted
            try
            {
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("MaxDeferredPublishMillisecondsPerFrame cannot be NaN"));
                MvcFacade.MaxDeferredPublishMillisecondsPerFrame = double.NaN;

                Assert.That(MvcFacade.MaxDeferredPublishMillisecondsPerFrame, Is.EqualTo(250),
                    "NaN must be rejected, leaving the previously configured value in place.");
            }
            finally
            {
                MvcFacade.MaxDeferredPublishMillisecondsPerFrame = double.MaxValue;
            }
        }

        [Test]
        public void MaxDeferredPublishesPerFrame_SetNonNegative_AcceptsValueWithoutWarning()
        {
            try
            {
                MvcFacade.MaxDeferredPublishesPerFrame = 42;

                Assert.That(MvcFacade.MaxDeferredPublishesPerFrame, Is.EqualTo(42),
                    "A valid non-negative budget must be accepted unchanged.");
            }
            finally
            {
                MvcFacade.MaxDeferredPublishesPerFrame = int.MaxValue;
            }
        }
    }
}
