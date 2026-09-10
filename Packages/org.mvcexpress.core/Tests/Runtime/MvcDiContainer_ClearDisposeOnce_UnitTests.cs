using System;
using System.Collections;
using mvcExpress.Internal.DependencyInjection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace mvcExpress.Tests
{
    // Regression coverage: Clear() used to call DisposeRegistrations
    // independently for each of the four internal dictionaries (logic objects, logic interfaces,
    // view objects, view interfaces), so one instance aliased under several keys/domains was
    // disposed once per dictionary it appeared in instead of once overall.
    [TestFixture]
    public class MvcDiContainer_ClearDisposeOnce_UnitTests
    {
        private interface IFoo { }
        private interface IBar { }

        // Throws if Dispose() is ever called a second time - the audit's own suggested
        // regression shape ("a non-idempotent disposable mapped under several aliases and domains").
        private sealed class NonIdempotentDisposable : IFoo, IBar, IDisposable
        {
            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
                if (DisposeCount > 1)
                {
                    throw new InvalidOperationException(
                        $"Dispose() called {DisposeCount} times - must only ever be called once.");
                }
            }
        }

        private interface ITrackedThing { }
        private sealed class TrackedProxyBehaviour : ProxyBehaviour, ITrackedThing { }

        private MvcDiContainer _container;
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _container = new MvcDiContainer();
        }

        [TearDown]
        public void TearDown()
        {
            _container?.Dispose();

            if (_go != null)
            {
                UnityEngine.Object.DestroyImmediate(_go);
                _go = null;
            }
        }

        [Test]
        public void Clear_InstanceAliasedAcrossAllFourDictionaries_DisposedExactlyOnce()
        {
            var instance = new NonIdempotentDisposable();

            // .ToLogic() -> _logicObjects, .ToLogicAs<IFoo>() -> _logicInterfaces,
            // .ToView() -> _viewObjects, .ToViewAs<IBar>() -> _viewInterfaces - all four
            // internal dictionaries end up holding a reference to the SAME instance.
            _container.Register(instance)
                .ToLogic()
                .ToLogicAs<IFoo>()
                .ToView()
                .ToViewAs<IBar>()
                .AsPermanent();

            _container.Clear();

            Assert.That(instance.DisposeCount, Is.EqualTo(1),
                "An instance registered under multiple keys/domains must be disposed exactly once by Clear().");
        }

        [UnityTest]
        public IEnumerator Clear_ProxyBehaviourAliasedUnderTwoKeys_GameObjectDestroyedExactlyOnce()
        {
            _go = new GameObject("ClearDisposeOnceProxy");
            var proxyBehaviour = _go.AddComponent<TrackedProxyBehaviour>();

            _container.Register(proxyBehaviour)
                .ToLogic()
                .ToLogicAs<ITrackedThing>()
                .AsPermanent();

            _container.Clear();

            // Destroy(gameObject) is deferred to end-of-frame in Play Mode - yield once so it
            // actually takes effect before checking Unity's overloaded null comparison.
            yield return null;

            Assert.That(proxyBehaviour == null, Is.True,
                "Clear() must destroy a ProxyBehaviour's GameObject through Unity's normal destroy " +
                "policy (not merely check IDisposable), even when aliased under multiple keys.");
        }
    }
}
