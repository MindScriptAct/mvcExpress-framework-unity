using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using mvcExpress;
using mvcExpress.Internal.Initialization;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests
{
    // Regression coverage: FacadeGlobalContainerApi.Register and
    // ModuleGlobalContainerApi.Register must never inject/OnInitialized() an instance before its
    // registration has actually committed. This is already fixed - RegistrationBuilder.Complete()
    // only invokes OnNonScopedCommitted (which both APIs wire to their InitializeRegisteredInstance)
    // AFTER RegisterInternal succeeds, and never at all for a .AsScoped() chain or a chain whose
    // terminal .As*() is never called.
    //
    // The scoped-throwaway scenario (the finding's 4th bullet) already has dedicated coverage in
    // Tests/Scenarios/ScopedLifetime/CompositionStyleScopedLifetimeScenarioTests.cs
    // (FacadeGlobalContainerApi_ScopedProxySeed_IsNotPrematurelyInitialized and the Module/Command
    // equivalents). This file locks in the other two bullets - no terminal call, and a failed
    // (duplicate-key) commit - which had no prior test coverage.
    [TestFixture]
    public class MvcGlobalRegistration_DeferredInit_UnitTests
    {
        // ── Test doubles ─────────────────────────────────────────────────────

        // Implements ISharedMarker too - DuplicateKeyFacade/DuplicateKeyModule below register two
        // instances of this class under that same marker interface, deliberately colliding.
        // RegistrationBuilder validates that the instance actually implements whatever it's
        // aliased to, so a plain marker interface can no longer be used as a bare, unimplemented key.
        public class TrackedLifecycleService : IMvcLifecycle, ISharedMarker
        {
            public bool OnInitializedCalled { get; private set; }
            public void OnInitialized() => OnInitializedCalled = true;
            public void OnCleanup() { }
        }

        // Shared key both registrations below collide on, forcing the second commit to fail.
        private interface ISharedMarker { }

        // Throws from OnInitialized() to exercise the post-commit failure path.
        public class ThrowingOnInitializedService : IMvcLifecycle
        {
            public void OnInitialized() => throw new System.InvalidOperationException("Boom.");
            public void OnCleanup() { }
        }

        private class NoTerminalFacade : MvcFacade
        {
            public TrackedLifecycleService Service { get; private set; }

            protected override void RegisterGlobalServices()
            {
                Service = new TrackedLifecycleService();
                GlobalDependencies.Register(Service).ToLogic(); // no .As*() - chain never completes
            }
        }

        private class DuplicateKeyFacade : MvcFacade
        {
            public TrackedLifecycleService First { get; private set; }
            public TrackedLifecycleService Second { get; private set; }

            protected override void RegisterGlobalServices()
            {
                First = new TrackedLifecycleService();
                GlobalDependencies.Register(First).ToLogicAs<ISharedMarker>().AsPermanent();

                Second = new TrackedLifecycleService();
                // Same key as First - RegisterInternal must throw here, before OnInitialized().
                GlobalDependencies.Register(Second).ToLogicAs<ISharedMarker>().AsPermanent();
            }
        }

        private class ThrowingOnInitializedFacade : MvcFacade
        {
            public ThrowingOnInitializedService Service { get; private set; }

            protected override void RegisterGlobalServices()
            {
                Service = new ThrowingOnInitializedService();
                GlobalDependencies.Register(Service).ToLogicAs<ThrowingOnInitializedService>().AsPermanent();
            }
        }

        private class NoTerminalModule : MvcModule
        {
            public TrackedLifecycleService Service { get; private set; }

            protected override void RegisterServices()
            {
                Service = new TrackedLifecycleService();
                Global.Register(Service).ToLogic(); // no .As*() - chain never completes
            }
        }

        private class DuplicateKeyModule : MvcModule
        {
            public TrackedLifecycleService First { get; private set; }
            public TrackedLifecycleService Second { get; private set; }

            protected override void RegisterServices()
            {
                First = new TrackedLifecycleService();
                Global.Register(First).ToLogicAs<ISharedMarker>().AsPermanent();

                Second = new TrackedLifecycleService();
                Global.Register(Second).ToLogicAs<ISharedMarker>().AsPermanent();
            }
        }

        private class ThrowingOnInitializedModule : MvcModule
        {
            public ThrowingOnInitializedService Service { get; private set; }

            protected override void RegisterServices()
            {
                Service = new ThrowingOnInitializedService();
                Global.Register(Service).ToLogicAs<ThrowingOnInitializedService>().AsPermanent();
            }
        }

        // ── Lifecycle ────────────────────────────────────────────────────────

        private readonly List<GameObject> _createdGameObjects = new List<GameObject>();

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

            AttributeScanner.Reset();
        }

        private T CreateFacade<T>() where T : MvcFacade
        {
            var go = new GameObject(typeof(T).Name);
            _createdGameObjects.Add(go);
            return go.AddComponent<T>();
        }

        private T CreateModule<T>() where T : MvcModule
        {
            var go = new GameObject(typeof(T).Name);
            _createdGameObjects.Add(go);
            return go.AddComponent<T>();
        }

        // ── Tests: FacadeGlobalContainerApi ─────────────────────────────────

        [Test]
        public void FacadeGlobalContainerApi_NoTerminalCalled_NeverInitializesOrRegisters()
        {
            var facade = CreateFacade<NoTerminalFacade>();

            Assert.IsFalse(facade.Service.OnInitializedCalled,
                "A chain with no terminal .As*() call must never reach OnInitialized() - the " +
                "registration itself never committed either.");
            Assert.IsFalse(MvcFacade.Global.TryResolve<TrackedLifecycleService>(out _),
                "An incomplete chain must never be resolvable from the container.");
        }

        [Test]
        public void FacadeGlobalContainerApi_DuplicateKeyRegistrationFails_SecondInstanceNeverInitialized()
        {
            LogAssert.Expect(LogType.Exception, new Regex("already registered in logic container"));

            var facade = CreateFacade<DuplicateKeyFacade>();

            Assert.IsTrue(facade.First.OnInitializedCalled,
                "Precondition: the first, non-colliding registration must have committed and initialized normally.");
            Assert.IsFalse(facade.Second.OnInitializedCalled,
                "A registration that fails at commit (duplicate key) must never reach OnInitialized() - " +
                "RegisterInternal throws before OnNonScopedCommitted is ever invoked.");
        }

        [Test]
        public void FacadeGlobalContainerApi_OnInitializedThrows_RegistrationIsRolledBack()
        {
            LogAssert.Expect(LogType.Exception, new Regex("Boom"));

            var facade = CreateFacade<ThrowingOnInitializedFacade>();

            Assert.IsFalse(MvcFacade.GlobalContainerOrNull.TryResolve<ThrowingOnInitializedService>(out _),
                "A registration whose OnInitialized() throws must be rolled back (removed from " +
                "the container), not left resolvable in a half-initialized, untracked state.");
        }

        // ── Tests: ModuleGlobalContainerApi ──────────────────────────────────

        [Test]
        public void ModuleGlobalContainerApi_NoTerminalCalled_NeverInitializesOrRegisters()
        {
            var module = CreateModule<NoTerminalModule>();

            Assert.IsFalse(module.Service.OnInitializedCalled,
                "A chain with no terminal .As*() call must never reach OnInitialized() - the " +
                "registration itself never committed either.");
            Assert.IsFalse(MvcFacade.Global.TryResolve<TrackedLifecycleService>(out _),
                "An incomplete chain must never be resolvable from the global container.");
        }

        [Test]
        public void ModuleGlobalContainerApi_DuplicateKeyRegistrationFails_SecondInstanceNeverInitialized()
        {
            // A module that throws during RegisterServices now self-destroys, logging this
            // error BEFORE the original exception propagates out to Unity's own engine-level log -
            // LogAssert.Expect enforces chronological order, so it must be declared first here.
            LogAssert.Expect(LogType.Error, new Regex("failed to initialize and will be destroyed"));
            LogAssert.Expect(LogType.Exception, new Regex("already registered in logic container"));

            var module = CreateModule<DuplicateKeyModule>();

            Assert.IsTrue(module.First.OnInitializedCalled,
                "Precondition: the first, non-colliding registration must have committed and initialized normally.");
            Assert.IsFalse(module.Second.OnInitializedCalled,
                "A registration that fails at commit (duplicate key) must never reach OnInitialized() - " +
                "RegisterInternal throws before OnNonScopedCommitted is ever invoked.");
        }

        [Test]
        public void ModuleGlobalContainerApi_OnInitializedThrows_RegistrationIsRolledBack()
        {
            // A module that throws during RegisterServices self-destroys, logging this
            // error BEFORE the original exception propagates out to Unity's own engine-level log -
            // LogAssert.Expect enforces chronological order, so it must be declared first here.
            LogAssert.Expect(LogType.Error, new Regex("failed to initialize and will be destroyed"));
            LogAssert.Expect(LogType.Exception, new Regex("Boom"));

            var module = CreateModule<ThrowingOnInitializedModule>();

            Assert.IsFalse(MvcFacade.Global.TryResolve<ThrowingOnInitializedService>(out _),
                "A registration whose OnInitialized() throws must be rolled back (removed from " +
                "the container), not left resolvable in a half-initialized, untracked state.");
        }
    }
}
