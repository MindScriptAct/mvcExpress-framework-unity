using NUnit.Framework;
using mvcExpress;
using mvcExpress.Internal.Initialization;
using System;

namespace mvcExpress.Tests
{
    public class AttributeMetadataTests
    {
        private class DummyProxy : Proxy {}
        private class DummyService {}
        private class DummyCommand : Command { public override void Execute() {} }
        private class DummyMediator : MediatorBehaviour {}
        private class DummyModule : MvcModule {}
        private struct DummyMessage : IMessage {} // IMessage required - BindAttribute.Validate() throws without it

        [Test]
        public void ProxyRegistrationMetadata_FallsBackToProxyTypeIfInterfacesNull()
        {
            var attr = new RegisterAttribute();
            var metadata = new ProxyRegistrationMetadata(typeof(DummyProxy), attr);

            Assert.That(metadata.LogicType, Is.EqualTo(typeof(DummyProxy)));
            Assert.That(metadata.ViewType, Is.EqualTo(typeof(DummyProxy)));
        }

        [Test]
        public void CommandBindingMetadata_MapsPropertiesCorrectly()
        {
            var attr = new BindAttribute(typeof(DummyMessage), typeof(DummyModule))
            {
                IsAsync = true,
                PoolSize = 5
            };

            var metadata = new CommandBindingMetadata(typeof(DummyCommand), attr);

            Assert.That(metadata.CommandType, Is.EqualTo(typeof(DummyCommand)));
            Assert.That(metadata.MessageType, Is.EqualTo(typeof(DummyMessage)));
            Assert.That(metadata.TargetModuleType, Is.EqualTo(typeof(DummyModule)));
            Assert.That(metadata.IsAsync, Is.True);
            Assert.That(metadata.PoolSize, Is.EqualTo(5u));
        }

        [Test]
        public void MediatorAttachmentMetadata_IsPrefabBased_ComputesCorrectly()
        {
            var attr1 = new AttachAttribute { PrefabPath = "Path/To/Prefab" };
            var meta1 = new MediatorAttachmentMetadata(typeof(DummyMediator), attr1);
            Assert.That(meta1.IsPrefabBased, Is.True);
            
            var attr2 = new AttachAttribute { FindInScene = true };
            var meta2 = new MediatorAttachmentMetadata(typeof(DummyMediator), attr2);
            Assert.That(meta2.IsPrefabBased, Is.False);

            var attr3 = new AttachPrefabAttribute();
            var meta3 = new MediatorAttachmentMetadata(typeof(DummyMediator), attr3);
            Assert.That(meta3.IsPrefabBased, Is.True);
            Assert.That(meta3.UsePrefabCatalog, Is.True);
        }
    }
}
