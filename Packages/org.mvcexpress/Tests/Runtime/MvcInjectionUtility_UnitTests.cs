using System;
using NUnit.Framework;
using mvcExpress;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;
using mvcExpress.Internal.Commands;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests
{
    public class MvcInjectionUtility_UnitTests
    {
        private class DummyLocalDependency {}
        private class AnotherDependency {}

        private class DummyTarget
        {
            [Inject]
            public DummyLocalDependency LocalDep { get; set; }
        }

        // optional = false means required (throws if missing)
        private class TargetWithRequired
        {
            [Inject(false)]
            public DummyLocalDependency RequiredDep;
        }

        // optional = true means silently skipped if missing
        private class TargetWithOptional
        {
            [Inject(true)]
            public DummyLocalDependency OptionalDep;
        }

        private class BaseTarget
        {
            [Inject]
            public DummyLocalDependency InheritedDep { get; set; }
        }

        private class DerivedTarget : BaseTarget
        {
            [Inject]
            public AnotherDependency OwnDep { get; set; }
        }

        private class TargetWithStatic
        {
            [Inject]
            public static DummyLocalDependency StaticDep { get; set; }

            [Inject]
            public DummyLocalDependency InstanceDep { get; set; }
        }

        private class GlobalDependency {}

        private class TargetWithGlobal
        {
            [InjectGlobal]
            public GlobalDependency GlobalDep { get; set; }
        }

        private class CommandWithTransientDependency : Command
        {
            [Inject] public DummyLocalDependency LocalDep;
            public override void Execute() {}
        }

        private class DummyModule : MvcModule {}

        private MvcDiContainer _container;
        private MvcMessageBus _messageBus;
        private MvcCommandProcessor _processor;

        [SetUp]
        public void SetUp()
        {
            _container = new MvcDiContainer();
            _messageBus = new MvcMessageBus();
            _processor = new MvcCommandProcessor(typeof(DummyModule), _container, _messageBus);
        }

        [TearDown]
        public void TearDown()
        {
            _processor?.Dispose();
            _messageBus?.Dispose();
            _container.Dispose();

            var facade = MvcFacade.InstanceOrNull;
            if (facade != null)
            {
                Object.DestroyImmediate(facade.gameObject);
            }
        }

        [Test]
        public void InjectMembers_Local_ResolvesAndAssignsProperty()
        {
            var dep = new DummyLocalDependency();
            _container.Register(dep).ToLogic().AsPersistent();

            var target = new DummyTarget();
            MvcInjectionUtility.InjectMembers(target, _container, useViewScope: false);

            Assert.That(target.LocalDep, Is.SameAs(dep),
                "InjectMembers must assign the registered instance to the [Inject] property.");
        }

        [Test]
        public void InjectMembers_MissingRequired_ThrowsInvalidOperationException()
        {
            // Container is empty - required dep is not registered
            var target = new TargetWithRequired();

            Assert.Throws<InvalidOperationException>(
                () => MvcInjectionUtility.InjectMembers(target, _container, useViewScope: false),
                "InjectMembers must throw when a required [Inject] dependency is not registered.");
        }

        [Test]
        public void InjectMembers_MissingOptional_SkipsWithoutThrowing()
        {
            // Container is empty - optional dep is not registered
            var target = new TargetWithOptional();

            Assert.DoesNotThrow(
                () => MvcInjectionUtility.InjectMembers(target, _container, useViewScope: false),
                "InjectMembers must not throw when an optional [Inject] dependency is missing.");

            Assert.That(target.OptionalDep, Is.Null,
                "Unregistered optional dependency must remain null.");
        }

        [Test]
        public void InjectMembers_NullTarget_IsNoOp()
        {
            // Null target must not throw
            Assert.DoesNotThrow(
                () => MvcInjectionUtility.InjectMembers(null, _container, useViewScope: false));
        }

        [Test]
        public void InjectMembers_NullContainer_IsNoOp()
        {
            var target = new DummyTarget();
            Assert.DoesNotThrow(
                () => MvcInjectionUtility.InjectMembers(target, null, useViewScope: false));
        }

        [Test]
        public void InjectMembers_InheritedMember_IsInjected()
        {
            var dep = new DummyLocalDependency();
            var another = new AnotherDependency();
            _container.Register(dep).ToLogic().AsPersistent();
            _container.Register(another).ToLogic().AsPersistent();

            var target = new DerivedTarget();
            MvcInjectionUtility.InjectMembers(target, _container, useViewScope: false);

            Assert.That(target.InheritedDep, Is.SameAs(dep),
                "Inherited [Inject] member from base class must be resolved.");
            Assert.That(target.OwnDep, Is.SameAs(another),
                "Own [Inject] member in derived class must also be resolved.");
        }

        [Test]
        public void InjectMembers_StaticMember_IsNotInjected()
        {
            TargetWithStatic.StaticDep = null; // ensure clean state

            var dep = new DummyLocalDependency();
            _container.Register(dep).ToLogic().AsPersistent();

            var target = new TargetWithStatic();
            MvcInjectionUtility.InjectMembers(target, _container, useViewScope: false);

            Assert.That(target.InstanceDep, Is.SameAs(dep),
                "Instance [Inject] member must be resolved.");
            Assert.That(TargetWithStatic.StaticDep, Is.Null,
                "Static [Inject] member must NOT be injected by the utility.");
        }

        [Test]
        public void InjectMembers_Global_ResolvesFromMvcFacadeGlobalContainer()
        {
            var dep = new GlobalDependency();
            MvcFacade.Global.Register(dep).ToLogic().AsPersistent();

            var target = new TargetWithGlobal();

            MvcInjectionUtility.InjectMembers(target, _container, useViewScope: false);

            Assert.That(target.GlobalDep, Is.SameAs(dep),
                "[InjectGlobal] must resolve from MvcFacade.Global, not the local container.");
        }

        [Test]
        public void InjectMembers_CommandWithTransientDependency_TracksDependencyForPoolInvalidation()
        {
            var dep = new DummyLocalDependency();
            _container.Register(dep).ToLogic().AsTransient();

            var pool = _processor.GetOrCreatePool<CommandWithTransientDependency>(1);
            var pooled = pool.Get();
            pool.Return(pooled);
            Assert.That(pool.GetStatistics().CurrentSize, Is.EqualTo(1),
                "Precondition: pool must contain one command before transient dependency removal.");

            var target = new CommandWithTransientDependency();
            MvcInjectionUtility.InjectMembers(target, _container, useViewScope: false, _processor);

            Assert.That(target.LocalDep, Is.SameAs(dep));

            _container.Unregister<DummyLocalDependency>();

            Assert.That(pool.GetStatistics().CurrentSize, Is.EqualTo(0),
                "InjectMembers must notify the command processor so transient removal clears dependent pools.");
        }

        [Test]
        public void PreWarmCache_WithKnownType_AllowsLaterInjection()
        {
            var dep = new DummyLocalDependency();
            _container.Register(dep).ToLogic().AsPersistent();

            MvcInjectionUtility.PreWarmCache(typeof(DummyTarget));

            var target = new DummyTarget();
            MvcInjectionUtility.InjectMembers(target, _container, useViewScope: false);

            Assert.That(target.LocalDep, Is.SameAs(dep));
        }

        [Test]
        public void PreWarmCacheFromAssemblies_WithTestAssembly_AllowsLaterInjection()
        {
            var dep = new DummyLocalDependency();
            _container.Register(dep).ToLogic().AsPersistent();

            MvcInjectionUtility.PreWarmCacheFromAssemblies(typeof(CommandWithTransientDependency).Assembly);

            var target = new CommandWithTransientDependency();
            MvcInjectionUtility.InjectMembers(target, _container, useViewScope: false);

            Assert.That(target.LocalDep, Is.SameAs(dep));
        }
    }
}
