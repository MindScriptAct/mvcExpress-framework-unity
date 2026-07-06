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
    }
}
