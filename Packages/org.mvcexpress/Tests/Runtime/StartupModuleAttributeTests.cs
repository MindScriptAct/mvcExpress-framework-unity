using NUnit.Framework;
using mvcExpress;
using mvcExpress.Internal.Initialization;
using System;

namespace mvcExpress.Tests
{
    [StartupModule]
    public class AutoStartModule : MvcModule { }

    [StartupModule(Order = 10)]
    public class LateStartModule : MvcModule { }

    [StartupModule(Order = -5)]
    public class EarlyStartModule : MvcModule { }

    public class PlainModule : MvcModule { }

    public class StartupModuleAttributeTests
    {
        [SetUp]
        [TearDown]
        public void ResetScanner()
        {
            AttributeScanner.Reset();
        }

        [Test]
        public void ScanAssemblies_PopulatesStartupModuleCache()
        {
            AttributeScanner.ScanAssemblies();

            var metadata = AttributeScanner.GetStartupModuleMetadata();

            Assert.That(metadata, Has.Some.Matches<StartupModuleMetadata>(item =>
                item.ModuleType == typeof(AutoStartModule)));
        }

        [Test]
        public void GetStartupModuleMetadata_ThrowsBeforeScan()
        {
            Assert.Throws<InvalidOperationException>(() => AttributeScanner.GetStartupModuleMetadata());
        }

        [Test]
        public void StartupModuleMetadata_OrderIsPreserved()
        {
            AttributeScanner.ScanAssemblies();

            var metadata = AttributeScanner.GetStartupModuleMetadata();

            Assert.That(metadata, Has.Some.Matches<StartupModuleMetadata>(item =>
                item.ModuleType == typeof(LateStartModule) && item.Order == 10));
            Assert.That(metadata, Has.Some.Matches<StartupModuleMetadata>(item =>
                item.ModuleType == typeof(EarlyStartModule) && item.Order == -5));
        }

        [Test]
        public void ScanAssemblies_DoesNotIncludeNonDecoratedModule()
        {
            AttributeScanner.ScanAssemblies();

            var metadata = AttributeScanner.GetStartupModuleMetadata();

            Assert.That(metadata, Has.None.Matches<StartupModuleMetadata>(item =>
                item.ModuleType == typeof(PlainModule)));
        }

        [Test]
        public void StartupModuleAttribute_Validate_ThrowsForNonModuleType()
        {
            var attr = new StartupModuleAttribute();

            Assert.Throws<InvalidOperationException>(() => attr.Validate(typeof(GlobalMockService)));
        }

        [Test]
        public void StartupModuleAttribute_Validate_AcceptsModuleSubclass()
        {
            var attr = new StartupModuleAttribute();

            Assert.DoesNotThrow(() => attr.Validate(typeof(AutoStartModule)));
        }

        [Test]
        public void Reset_ClearsStartupModuleCache()
        {
            AttributeScanner.ScanAssemblies();
            AttributeScanner.Reset();

            AttributeScanner.ScanAssemblies();

            var metadata = AttributeScanner.GetStartupModuleMetadata();
            Assert.That(metadata, Has.Some.Matches<StartupModuleMetadata>(item =>
                item.ModuleType == typeof(AutoStartModule)));
        }
    }
}
