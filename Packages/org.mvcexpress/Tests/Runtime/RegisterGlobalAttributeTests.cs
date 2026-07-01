using NUnit.Framework;
using mvcExpress;
using mvcExpress.Internal.Initialization;
using System;

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
        public void Reset_ClearsGlobalCache()
        {
            AttributeScanner.ScanAssemblies();
            AttributeScanner.Reset();

            AttributeScanner.ScanAssemblies();

            var metadata = AttributeScanner.GetGlobalRegistrationMetadata();
            Assert.That(metadata, Has.Some.Matches<GlobalRegistrationMetadata>(item =>
                item.ConcreteType == typeof(GlobalMockProxy)));
        }
    }
}
