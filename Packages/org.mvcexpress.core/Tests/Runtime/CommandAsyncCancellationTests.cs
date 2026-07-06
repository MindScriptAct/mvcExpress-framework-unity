using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests
{
    [TestFixture]
    public class CommandAsyncCancellationTests
    {
        private class TestModule : MvcModule { }

        private class TokenExposingCommand : CommandAsync
        {
            public CancellationToken ObservedToken { get; private set; }

            public override Task ExecuteAsync()
            {
                ObservedToken = CancelToken;
                return Task.CompletedTask;
            }
        }

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
        public void ExecuteAsync_ReadsCancelToken_MatchesModulesToken()
        {
            _moduleGo = new GameObject("TestModule");
            var module = _moduleGo.AddComponent<TestModule>();

            var cmd = new TokenExposingCommand();
            cmd.Initialize(module, module.DiContainer, module.MessageBus, module.CommandProcessor);
            cmd.ExecuteAsync().GetAwaiter().GetResult();

            Assert.AreEqual(module.CancelToken, cmd.ObservedToken);
        }
    }
}
