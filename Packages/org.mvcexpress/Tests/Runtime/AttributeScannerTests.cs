using NUnit.Framework;
using mvcExpress;
using mvcExpress.Internal.Initialization;
using System;

namespace mvcExpress.Tests
{
    [Register(typeof(DummyModule))]
    public class MockScannedProxy : Proxy {}

    public class DummyModule : MvcModule {}

    public class AttributeScannerMetadataModule : MvcModule {}

    [Register(typeof(AttributeScannerMetadataModule))]
    public class MockScannedService {}

    public readonly struct MockScannedMessage : IMessage {}

    [Bind(typeof(MockScannedMessage), typeof(AttributeScannerMetadataModule))]
    public class MockScannedCommand : Command
    {
        public override void Execute() {}
    }

    [Attach(typeof(AttributeScannerMetadataModule), FindInScene = true)]
    public class MockScannedMediator : MediatorBehaviour {}

    public class AttributeScannerTests
    {
        [SetUp]
        [TearDown]
        public void ResetScanner()
        {
            AttributeScanner.Reset();
        }

        [Test]
        public void ScanAssemblies_ExecutesOnceAndPopulatesIsScanned()
        {
            Assert.That(AttributeScanner.IsScanned, Is.False);
            
            AttributeScanner.ScanAssemblies();
            
            Assert.That(AttributeScanner.IsScanned, Is.True);
        }

        [Test]
        public void GetProxyMetadata_ThrowsIfNullModule()
        {
            AttributeScanner.ScanAssemblies();
            Assert.Throws<ArgumentNullException>(() => AttributeScanner.GetProxyMetadata(null));
        }

        [Test]
        public void GetProxyMetadata_RetrievesRegisteredProxy()
        {
            AttributeScanner.ScanAssemblies();
            
            var metadata = AttributeScanner.GetProxyMetadata(typeof(DummyModule));
            
            Assert.That(metadata, Is.Not.Null);
        }

        [Test]
        public void GetServiceMetadata_RetrievesRegisteredService()
        {
            AttributeScanner.ScanAssemblies();

            var metadata = AttributeScanner.GetServiceMetadata(typeof(AttributeScannerMetadataModule));

            Assert.That(metadata, Has.Some.Matches<ServiceRegistrationMetadata>(item =>
                item.ServiceType == typeof(MockScannedService) &&
                item.TargetModuleType == typeof(AttributeScannerMetadataModule)));
        }

        [Test]
        public void GetCommandMetadata_RetrievesBoundCommand()
        {
            AttributeScanner.ScanAssemblies();

            var metadata = AttributeScanner.GetCommandMetadata(typeof(AttributeScannerMetadataModule));

            Assert.That(metadata, Has.Some.Matches<CommandBindingMetadata>(item =>
                item.CommandType == typeof(MockScannedCommand) &&
                item.MessageType == typeof(MockScannedMessage) &&
                item.TargetModuleType == typeof(AttributeScannerMetadataModule)));
        }

        [Test]
        public void GetMediatorMetadata_RetrievesAttachedMediator()
        {
            AttributeScanner.ScanAssemblies();

            var metadata = AttributeScanner.GetMediatorMetadata(typeof(AttributeScannerMetadataModule));

            Assert.That(metadata, Has.Some.Matches<MediatorAttachmentMetadata>(item =>
                item.MediatorType == typeof(MockScannedMediator) &&
                item.TargetModuleType == typeof(AttributeScannerMetadataModule) &&
                item.FindInScene));
        }

        [Test]
        public void Reset_ClearsState()
        {
            AttributeScanner.ScanAssemblies();
            Assert.That(AttributeScanner.IsScanned, Is.True);

            AttributeScanner.Reset();

            Assert.That(AttributeScanner.IsScanned, Is.False);
        }

        [Test]
        public void ScanAssemblies_CalledTwice_IsIdempotent()
        {
            AttributeScanner.ScanAssemblies();
            var first = AttributeScanner.GetProxyMetadata(typeof(DummyModule));

            // Second call must be a no-op and must not throw or duplicate metadata
            Assert.DoesNotThrow(() => AttributeScanner.ScanAssemblies(),
                "Second call to ScanAssemblies must not throw.");
            Assert.That(AttributeScanner.IsScanned, Is.True);

            var second = AttributeScanner.GetProxyMetadata(typeof(DummyModule));
            Assert.That(second.Count, Is.EqualTo(first.Count),
                "Metadata count must not change on a redundant scan.");
        }
    }
}
