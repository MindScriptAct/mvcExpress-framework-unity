using NUnit.Framework;
using UnityEngine;

namespace mvcExpress.Tests.Scenarios
{
    [TestFixture]
    [Category("Scenario")]
    public class MultiMappingWrapperScenarioTests
    {
        public interface IReadOnlyWrapperThing { }
        public interface IMeasurementWrapperThing { }

        public sealed class WrapperMultiMapProxy : Proxy, IReadOnlyWrapperThing, IMeasurementWrapperThing { }

        public sealed class MultiMappingModule : MvcModule
        {
            protected override void RegisterProxies()
            {
                Container.Register(new WrapperMultiMapProxy())
                    .ToLogic()
                    .ToLogicAs<IReadOnlyWrapperThing>()
                    .ToLogicAs<IMeasurementWrapperThing>()
                    .AsPermanent();
            }
        }

        private GameObject _moduleGo;

        [TearDown]
        public void TearDown()
        {
            if (_moduleGo != null) Object.DestroyImmediate(_moduleGo);
            var facade = MvcFacade.InstanceOrNull;
            if (facade != null) Object.DestroyImmediate(facade.gameObject);
        }

        [Test]
        public void ModuleContainer_RegisterProxies_MultiMappingChain_AllTypesResolve()
        {
            _moduleGo = new GameObject(nameof(MultiMappingModule));
            var module = _moduleGo.AddComponent<MultiMappingModule>();

            Assert.That(module.DiContainer.Resolve<WrapperMultiMapProxy>(), Is.Not.Null);
            Assert.That(module.DiContainer.Resolve<IReadOnlyWrapperThing>(), Is.Not.Null);
            Assert.That(module.DiContainer.Resolve<IMeasurementWrapperThing>(), Is.Not.Null);
            Assert.That(module.DiContainer.Resolve<IReadOnlyWrapperThing>(),
                Is.SameAs(module.DiContainer.Resolve<IMeasurementWrapperThing>()),
                "Both interfaces must resolve to the same shared instance.");
        }
    }
}
