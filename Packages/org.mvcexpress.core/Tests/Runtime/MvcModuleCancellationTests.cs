using System.Threading;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests
{
    [TestFixture]
    public class MvcModuleCancellationTests
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
        public void CancelToken_AfterAwake_IsNotYetCancelled()
        {
            _moduleGo = new GameObject("TestModule");
            var module = _moduleGo.AddComponent<TestModule>();

            Assert.IsFalse(module.CancelToken.IsCancellationRequested);
            Assert.IsTrue(module.CancelToken.CanBeCanceled);
        }

        [Test]
        public void CancelToken_CapturedBeforeDestroy_IsCancelledAfterOnDestroy()
        {
            _moduleGo = new GameObject("TestModule");
            var module = _moduleGo.AddComponent<TestModule>();

            // Capture the struct value before teardown - this is exactly what
            // MvcCommandBase.Initialize() does for a running command in Task 3.
            var capturedToken = module.CancelToken;

            Object.DestroyImmediate(_moduleGo);
            _moduleGo = null; // prevent TearDown from double-destroying

            Assert.IsTrue(capturedToken.IsCancellationRequested);
        }
    }
}
