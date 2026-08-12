using System.Text.RegularExpressions;
using mvcExpress.Internal.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests
{
    [TestFixture]
    public class ServiceMapping_ResolutionStrategies_UnitTests
    {
        private sealed class DummyService : MonoBehaviour
        {
        }

        private interface IDummyInterface
        {
        }

        private GameObject _go;
        private DummyService _service;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject(nameof(ServiceMapping_ResolutionStrategies_UnitTests));
            _service = _go.AddComponent<DummyService>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void ResolveLogicType_Strategy2_ValidTypeName_ResolvesSpecifiedType()
        {
            var mapping = new ServiceMapping
            {
                Service = _service,
                LogicTypeName = typeof(DummyService).AssemblyQualifiedName,
            };

            var resolved = mapping.ResolveLogicType();

            Assert.That(resolved, Is.EqualTo(typeof(DummyService)),
                "Strategy 2: when LogicTypeName is a valid assembly-qualified name, ResolveLogicType " +
                "must use Type.GetType(LogicTypeName) rather than falling back to Service.GetType().");
        }

        [Test]
        public void ResolveViewType_Strategy2_ValidTypeName_ResolvesSpecifiedType()
        {
            var mapping = new ServiceMapping
            {
                Service = _service,
                ViewTypeName = typeof(DummyService).AssemblyQualifiedName,
            };

            var resolved = mapping.ResolveViewType();

            Assert.That(resolved, Is.EqualTo(typeof(DummyService)),
                "Strategy 2: ResolveViewType must resolve a valid ViewTypeName via Type.GetType.");
        }

        [Test]
        public void ResolveLogicType_Strategy3_UnresolvableTypeName_ReturnsNullAndLogsError()
        {
            var mapping = new ServiceMapping
            {
                Service = _service,
                LogicTypeName = "Totally.Fake.Namespace.DoesNotExist, NoSuchAssembly",
            };

            LogAssert.Expect(LogType.Error, new Regex("Critical: Custom LOGIC type"));

            var resolved = mapping.ResolveLogicType(typeof(ServiceMapping_ResolutionStrategies_UnitTests));

            Assert.That(resolved, Is.Null,
                "Strategy 3: when LogicTypeName cannot be resolved by Type.GetType and Service is still " +
                "present, ResolveLogicType must return null (not silently fall back to Service.GetType()) " +
                "so the module fails to initialize loudly rather than registering the wrong type.");
        }

        [Test]
        public void ResolveViewType_Strategy3_UnresolvableTypeName_ReturnsNullAndLogsError()
        {
            var mapping = new ServiceMapping
            {
                Service = _service,
                ViewTypeName = "Totally.Fake.Namespace.DoesNotExist, NoSuchAssembly",
            };

            LogAssert.Expect(LogType.Error, new Regex("Critical: Custom VIEW type"));

            var resolved = mapping.ResolveViewType(typeof(ServiceMapping_ResolutionStrategies_UnitTests));

            Assert.That(resolved, Is.Null,
                "Strategy 3: ResolveViewType must return null for an unresolvable ViewTypeName, matching " +
                "ResolveLogicType's contract, and must log a diagnostic error explaining the failure.");
        }

        [Test]
        public void ResolveLogicType_Strategy1_EmptyTypeName_FallsBackToServiceConcreteType()
        {
            var mapping = new ServiceMapping
            {
                Service = _service,
                LogicTypeName = "",
            };

            var resolved = mapping.ResolveLogicType();

            Assert.That(resolved, Is.EqualTo(typeof(DummyService)),
                "Precondition/contrast case: Strategy 1 (empty LogicTypeName) must fall back to " +
                "Service.GetType() - this is the already-tested path, included here for contrast with " +
                "strategies 2 and 3 in the same file.");
        }
    }
}
