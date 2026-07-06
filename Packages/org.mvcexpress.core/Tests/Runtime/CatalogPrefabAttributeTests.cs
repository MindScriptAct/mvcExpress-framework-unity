using NUnit.Framework;
using mvcExpress;
using mvcExpress.Tests.Fakes;
using System;

namespace mvcExpress.Tests
{
    public class CatalogPrefabAttributeTests
    {
        private abstract class AbstractMediator : MediatorBehaviour { }

        public class NotAMediator { }

        [Test]
        public void Validate_AllowsConcreteMediatorBehaviour()
        {
            var attr = new CatalogPrefabAttribute();

            Assert.DoesNotThrow(() => attr.Validate(typeof(SpyMediator)));
        }

        [Test]
        public void Validate_ThrowsWhenNotMediatorBehaviour()
        {
            var attr = new CatalogPrefabAttribute();

            var ex = Assert.Throws<InvalidOperationException>(() => attr.Validate(typeof(NotAMediator)));
            Assert.That(ex.Message, Does.Contain("MediatorBehaviour"));
        }

        [Test]
        public void Validate_ThrowsWhenAbstract()
        {
            var attr = new CatalogPrefabAttribute();

            var ex = Assert.Throws<InvalidOperationException>(() => attr.Validate(typeof(AbstractMediator)));
            Assert.That(ex.Message, Does.Contain("abstract"));
        }

        [Test]
        public void Validate_ThrowsWhenTypeIsNull()
        {
            var attr = new CatalogPrefabAttribute();

            Assert.Throws<ArgumentNullException>(() => attr.Validate(null));
        }
    }
}
