using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests
{
    [TestFixture]
    public class MvcCommandProcessorCancellationTests
    {
        private class TestModule : MvcModule { }

        private GameObject _moduleGo;

        [TearDown]
        public void TearDown()
        {
            if (_moduleGo != null)
            {
                Object.DestroyImmediate(_moduleGo);
                _moduleGo = null;
            }

            var facade = MvcFacade.InstanceOrNull;
            if (facade != null)
            {
                Object.DestroyImmediate(facade.gameObject);
            }
        }

        [Test]
        public void CancelToken_MatchesOwningModulesToken()
        {
            _moduleGo = new GameObject("TestModule");
            var module = _moduleGo.AddComponent<TestModule>();

            Assert.AreEqual(module.CancelToken, module.CommandProcessor.CancelToken);
        }
    }
}
