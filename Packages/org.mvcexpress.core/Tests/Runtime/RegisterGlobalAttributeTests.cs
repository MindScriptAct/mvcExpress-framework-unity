using NUnit.Framework;
using mvcExpress;
using mvcExpress.Internal.Initialization;
using System;
using UnityEngine;

namespace mvcExpress.Tests
{
    [RegisterGlobal]
    public class GlobalMockProxy : Proxy { }

    public interface IGlobalMockProxyInterface { }

    [RegisterGlobal(LogicInterface = typeof(IGlobalMockProxyInterface))]
    public class GlobalMockProxyWithInterface : Proxy, IGlobalMockProxyInterface { }

    [RegisterGlobal]
    public class GlobalMockService { }

    public class NoScopeGlobalTarget { }

    [RegisterGlobal]
    public class GlobalMockProxyBehaviour : ProxyBehaviour
    {
        public bool Initialized { get; private set; }
        protected override void OnInitialized() => Initialized = true;
    }

    [RegisterGlobal]
    public class GlobalMockMonoBehaviourService : MonoBehaviour, IMvcLifecycle
    {
        public bool Initialized { get; private set; }
        public void OnInitialized() => Initialized = true;
        public void OnCleanup() { }
    }

    [RegisterGlobal(Lifecycle = RegistrationLifecycle.Scoped)]
    public class GlobalMockScopedProxy : Proxy { }

    [RegisterGlobal(Lifecycle = RegistrationLifecycle.Scoped)]
    public class GlobalMockScopedService : IMvcLifecycle
    {
        public static int InitializedCount;
        public void OnInitialized() => InitializedCount++;
        public void OnCleanup() { }
    }

    public interface IReadOnlyGlobalStackedThing { }
    public interface IMeasurementGlobalStackedThing { }

    [RegisterGlobal(LogicInterface = typeof(IReadOnlyGlobalStackedThing))]
    [RegisterGlobal(LogicInterface = typeof(IMeasurementGlobalStackedThing))]
    public class GlobalStackedInterfaceProxy : Proxy, IReadOnlyGlobalStackedThing, IMeasurementGlobalStackedThing { }

    // NOT decorated with [RegisterGlobal] - unlike [Register] (module-scoped, only drained when its
    // target module is instantiated), [RegisterGlobal] has no scoping: a decorated type is drained
    // into EVERY MvcFacade any test creates, for the rest of the test run. A deliberately-broken
    // global fixture (same interface stacked twice, or conflicting Lifecycle values) would hard-throw
    // during every unrelated test's facade boot, not just its own. That guarantee is instead verified
    // directly against AttributeGroupingUtility.ResolveGroupLifecycle in
    // RegisterGlobalAttributeStackingBehaviourTests.cs, with zero scanning/drain side effects. This
    // class exists only to give that direct test a labeled Type argument.
    public class GlobalLifecycleConflictProxy : Proxy, IReadOnlyGlobalStackedThing, IMeasurementGlobalStackedThing { }

    public class RegisterGlobalAttributeTests
    {
        [SetUp]
        [TearDown]
        public void ResetScanner()
        {
            AttributeScanner.Reset();
        }

        [Test]
        public void ScanAssemblies_PopulatesGlobalCache()
        {
            AttributeScanner.ScanAssemblies();

            var metadata = AttributeScanner.GetGlobalRegistrationMetadata();

            Assert.That(metadata, Has.Some.Matches<GlobalRegistrationMetadata>(item =>
                item.ConcreteType == typeof(GlobalMockProxy)));
        }

        [Test]
        public void GetGlobalRegistrationMetadata_ThrowsBeforeScan()
        {
            Assert.Throws<InvalidOperationException>(() => AttributeScanner.GetGlobalRegistrationMetadata());
        }

        [Test]
        public void GlobalRegistrationMetadata_ProxyIsMarkedAsProxy()
        {
            AttributeScanner.ScanAssemblies();

            var metadata = AttributeScanner.GetGlobalRegistrationMetadata();

            Assert.That(metadata, Has.Some.Matches<GlobalRegistrationMetadata>(item =>
                item.ConcreteType == typeof(GlobalMockProxy) && item.IsProxy));
        }

        [Test]
        public void GlobalRegistrationMetadata_ServiceIsNotMarkedAsProxy()
        {
            AttributeScanner.ScanAssemblies();

            var metadata = AttributeScanner.GetGlobalRegistrationMetadata();

            Assert.That(metadata, Has.Some.Matches<GlobalRegistrationMetadata>(item =>
                item.ConcreteType == typeof(GlobalMockService) && !item.IsProxy));
        }

        [Test]
        public void GlobalRegistrationMetadata_LogicInterfacePreserved()
        {
            AttributeScanner.ScanAssemblies();

            var metadata = AttributeScanner.GetGlobalRegistrationMetadata();

            Assert.That(metadata, Has.Some.Matches<GlobalRegistrationMetadata>(item =>
                item.ConcreteType == typeof(GlobalMockProxyWithInterface) &&
                item.LogicType == typeof(IGlobalMockProxyInterface)));
        }

        [Test]
        public void RegisterGlobalAttribute_Validate_ThrowsWhenNoScope()
        {
            var attr = new RegisterGlobalAttribute { RegisterToLogic = false, RegisterToView = false };

            Assert.Throws<InvalidOperationException>(() => attr.Validate(typeof(GlobalMockService)));
        }

        [Test]
        public void RegisterGlobalAttribute_Validate_ThrowsWhenLogicInterfaceNotImplemented()
        {
            var attr = new RegisterGlobalAttribute { LogicInterface = typeof(IGlobalMockProxyInterface) };

            Assert.Throws<InvalidOperationException>(() => attr.Validate(typeof(GlobalMockService)));
        }

        [Test]
        public void GlobalRegistrationMetadata_ProxyBehaviourIsMarkedAsProxyAndMonoBehaviour()
        {
            AttributeScanner.ScanAssemblies();

            var metadata = AttributeScanner.GetGlobalRegistrationMetadata();

            Assert.That(metadata, Has.Some.Matches<GlobalRegistrationMetadata>(item =>
                item.ConcreteType == typeof(GlobalMockProxyBehaviour) && item.IsProxy && item.IsMonoBehaviour));
        }

        [Test]
        public void GlobalRegistrationMetadata_MonoBehaviourServiceIsMonoBehaviourNotProxy()
        {
            AttributeScanner.ScanAssemblies();

            var metadata = AttributeScanner.GetGlobalRegistrationMetadata();

            Assert.That(metadata, Has.Some.Matches<GlobalRegistrationMetadata>(item =>
                item.ConcreteType == typeof(GlobalMockMonoBehaviourService) && !item.IsProxy && item.IsMonoBehaviour));
        }

        [Test]
        public void GlobalRegistrationMetadata_PlainProxyIsNotMarkedAsMonoBehaviour()
        {
            AttributeScanner.ScanAssemblies();

            var metadata = AttributeScanner.GetGlobalRegistrationMetadata();

            Assert.That(metadata, Has.Some.Matches<GlobalRegistrationMetadata>(item =>
                item.ConcreteType == typeof(GlobalMockProxy) && !item.IsMonoBehaviour));
        }

        [Test]
        public void Reset_ClearsGlobalCache()
        {
            AttributeScanner.ScanAssemblies();
            AttributeScanner.Reset();

            AttributeScanner.ScanAssemblies();

            var metadata = AttributeScanner.GetGlobalRegistrationMetadata();
            Assert.That(metadata, Has.Some.Matches<GlobalRegistrationMetadata>(item =>
                item.ConcreteType == typeof(GlobalMockProxy)));
        }

        [Test]
        public void RegisterGlobalAttribute_Validate_ThrowsWhenScopedOnProxyBehaviour()
        {
            var attr = new RegisterGlobalAttribute { Lifecycle = RegistrationLifecycle.Scoped };

            var ex = Assert.Throws<InvalidOperationException>(() => attr.Validate(typeof(GlobalMockProxyBehaviour)));
            Assert.That(ex.Message, Does.Contain("Scoped"));
        }

        [Test]
        public void RegisterGlobalAttribute_Validate_ThrowsWhenScopedOnMonoBehaviourService()
        {
            var attr = new RegisterGlobalAttribute { Lifecycle = RegistrationLifecycle.Scoped };

            var ex = Assert.Throws<InvalidOperationException>(() => attr.Validate(typeof(GlobalMockMonoBehaviourService)));
            Assert.That(ex.Message, Does.Contain("Scoped"));
        }

        [Test]
        public void RegisterGlobalAttribute_Validate_AllowsScopedOnPlainProxy()
        {
            var attr = new RegisterGlobalAttribute { Lifecycle = RegistrationLifecycle.Scoped };

            Assert.DoesNotThrow(() => attr.Validate(typeof(GlobalMockProxy)));
        }

        [Test]
        public void GlobalRegistrationMetadata_ExposesLifecycleFromAttribute()
        {
            AttributeScanner.ScanAssemblies();

            var metadata = AttributeScanner.GetGlobalRegistrationMetadata();

            Assert.That(metadata, Has.Some.Matches<GlobalRegistrationMetadata>(item =>
                item.ConcreteType == typeof(GlobalMockScopedProxy) && item.Lifecycle == RegistrationLifecycle.Scoped));
        }

        [Test]
        public void ScanAssemblies_StackedRegisterGlobalAttributes_ProducesTwoMetadataEntries()
        {
            AttributeScanner.ScanAssemblies();

            var metadata = AttributeScanner.GetGlobalRegistrationMetadata();
            int count = 0;
            foreach (var item in metadata)
            {
                if (item.ConcreteType == typeof(GlobalStackedInterfaceProxy))
                    count++;
            }

            Assert.That(count, Is.EqualTo(2),
                "Stacked [RegisterGlobal] attributes on one class must produce one metadata entry per " +
                "attribute, mirroring [Register]'s ScanForRegisterAttribute (plural GetCustomAttributes).");
        }
    }
}
