using NUnit.Framework;
using mvcExpress.Internal.Proxy;
using mvcExpress;
using UnityEngine;

namespace mvcExpress.Tests
{
    public class ProxyMappingTests
    {
        private class MyMockProxy : ProxyBehaviour {}

        // Track any GameObject created per test so TearDown can always clean it up,
        // even if an assertion fails mid-test.
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
                _go = null;
            }
        }

        [Test]
        public void ResolveLogicType_ValidString_ReturnsType()
        {
            var mapping = new ProxyMapping
            {
                LogicTypeName = typeof(MyMockProxy).AssemblyQualifiedName
            };

            var type = mapping.ResolveLogicType();
            Assert.That(type, Is.EqualTo(typeof(MyMockProxy)));
        }

        [Test]
        public void IsValid_AllConditionsMet_ReturnsTrue()
        {
            _go = new GameObject();
            var mapping = new ProxyMapping
            {
                Proxy = _go.AddComponent<MyMockProxy>(),
                RegisterToLogic = true,
                LogicTypeName = typeof(MyMockProxy).AssemblyQualifiedName
            };

            Assert.That(mapping.IsValid(), Is.True);
        }

        [Test]
        public void IsValid_MissingProxy_ReturnsFalse()
        {
            var mapping = new ProxyMapping
            {
                Proxy = null,
                RegisterToLogic = true,
                LogicTypeName = typeof(MyMockProxy).AssemblyQualifiedName
            };

            Assert.That(mapping.IsValid(), Is.False);
        }

        [Test]
        public void IsValid_MissingTypeName_ReturnsFalse()
        {
            _go = new GameObject();
            var mapping = new ProxyMapping
            {
                Proxy = _go.AddComponent<MyMockProxy>(),
                RegisterToLogic = true,
                LogicTypeName = ""
            };

            Assert.That(mapping.IsValid(), Is.False);
        }

        [Test]
        public void IsValid_BothLayersFalse_ReturnsFalse()
        {
            _go = new GameObject();
            var mapping = new ProxyMapping
            {
                Proxy = _go.AddComponent<MyMockProxy>(),
                RegisterToLogic = false,
                RegisterToView = false,
                LogicTypeName = typeof(MyMockProxy).AssemblyQualifiedName
            };

            Assert.That(mapping.IsValid(), Is.False,
                "A mapping that registers to neither layer is not usable.");
        }

        [Test]
        public void ResolveLogicType_InvalidTypeName_ReturnsNull()
        {
            var mapping = new ProxyMapping
            {
                LogicTypeName = "This.Type.Does.Not.Exist, SomeMissingAssembly"
            };

            var type = mapping.ResolveLogicType();
            Assert.That(type, Is.Null,
                "An unresolvable type name must return null without throwing.");
        }
    }
}
