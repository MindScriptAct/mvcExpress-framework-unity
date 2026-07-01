using NUnit.Framework;
using mvcExpress.Internal.Services;
using UnityEngine;

namespace mvcExpress.Tests
{
    public class ServiceMappingTests
    {
        private class MockServiceBehaviour : MonoBehaviour {}

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
        public void ResolveLogicType_EmptyLogicTypeName_FallsBackToConcreteType()
        {
            _go = new GameObject();
            var mapping = new ServiceMapping
            {
                Service = _go.AddComponent<MockServiceBehaviour>(),
                LogicTypeName = ""
            };

            var type = mapping.ResolveLogicType();
            Assert.That(type, Is.EqualTo(typeof(MockServiceBehaviour)));
        }

        [Test]
        public void IsValid_NoTypeNamesButServiceAssigned_ReturnsTrue()
        {
            _go = new GameObject();
            var mapping = new ServiceMapping
            {
                Service = _go.AddComponent<MockServiceBehaviour>(),
                RegisterToLogic = true,
                LogicTypeName = ""
            };

            Assert.That(mapping.IsValid(), Is.True);
        }

        [Test]
        public void IsValid_ServiceNull_ReturnsFalse()
        {
            var mapping = new ServiceMapping
            {
                Service = null,
                RegisterToLogic = true
            };

            Assert.That(mapping.IsValid(), Is.False);
        }

        [Test]
        public void IsValid_BothLayersFalse_ReturnsFalse()
        {
            _go = new GameObject();
            var mapping = new ServiceMapping
            {
                Service = _go.AddComponent<MockServiceBehaviour>(),
                RegisterToLogic = false,
                RegisterToView = false
            };

            Assert.That(mapping.IsValid(), Is.False,
                "A mapping that registers to neither layer is not usable.");
        }
    }
}
