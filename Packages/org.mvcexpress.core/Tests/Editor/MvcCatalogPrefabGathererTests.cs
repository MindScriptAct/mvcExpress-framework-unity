using NUnit.Framework;
using mvcExpress.Editor.Core;

namespace mvcExpress.Editor.Tests
{
    public class MvcCatalogPrefabGathererTests
    {
        [Test]
        public void Scan_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => MvcCatalogPrefabGatherer.Scan());
        }

        [Test]
        public void Scan_ReturnsNonNullResult()
        {
            var result = MvcCatalogPrefabGatherer.Scan();

            Assert.That(result, Is.Not.Null);
        }
    }
}
