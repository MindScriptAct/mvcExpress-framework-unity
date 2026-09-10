using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Initialization;
using mvcExpress.Internal.Messaging;

namespace mvcExpress.Tests
{
    [TestFixture]
    public class MediatorRegistrarTests
    {
        // ── Test doubles ──────────────────────────────────────────────────────

        // Module stub that skips Awake/OnDestroy entirely so the framework singleton
        // (MvcFacade) is never touched - matches the pattern in MediatorBehaviourTests.
        private class NoInitModule : MvcModule
        {
            protected override void Awake() { }
            protected override void OnDestroy() { }
        }

        private class RecordingMediator : MediatorBehaviour
        {
            public int OnCleanupCallCount { get; private set; }
            protected override void OnCleanup() => OnCleanupCallCount++;
        }

        // Throws from OnCleanup() - used to prove one mediator's failure cannot block
        // MediatorRegistrar.Cleanup()'s loop for its siblings (COR-16).
        private class ThrowingOnCleanupMediator : MediatorBehaviour
        {
            protected override void OnCleanup() => throw new InvalidOperationException("Boom.");
        }

        // ── Fields ────────────────────────────────────────────────────────────

        private GameObject _moduleGo;
        private NoInitModule _module;
        private MvcDiContainer _container;
        private MvcMessageBus _messageBus;
        private readonly List<GameObject> _trackedGameObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _moduleGo = new GameObject(nameof(NoInitModule));
            _module = _moduleGo.AddComponent<NoInitModule>();
            _container = new MvcDiContainer();
            _messageBus = new MvcMessageBus();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _trackedGameObjects)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _trackedGameObjects.Clear();

            if (_moduleGo != null)
            {
                Object.DestroyImmediate(_moduleGo);
                _moduleGo = null;
            }

            _messageBus?.Dispose();
            _container?.Dispose();
        }

        private T CreateMediator<T>(string name = null) where T : MediatorBehaviour
        {
            var go = new GameObject(name ?? typeof(T).Name);
            _trackedGameObjects.Add(go);
            return go.AddComponent<T>();
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Test]
        public void Cleanup_OneMediatorOnCleanupThrows_SiblingMediatorsStillCleanedUp()
        {
            // MediatorRegistrar.Cleanup() used to call each runtime mediator's CleanupMediator()
            // with no try/catch, so one throwing mediator aborted the whole loop - every mediator
            // after it in iteration order, plus both Clear() calls at the end, were skipped (COR-16).
            var registrar = new MediatorRegistrar(typeof(NoInitModule), _container, _messageBus, _module);

            var before = CreateMediator<RecordingMediator>("Before");
            var throwing = CreateMediator<ThrowingOnCleanupMediator>("Throwing");
            var after = CreateMediator<RecordingMediator>("After");

            Assert.IsTrue(registrar.AddRuntimeMediator(before));
            Assert.IsTrue(registrar.AddRuntimeMediator(throwing));
            Assert.IsTrue(registrar.AddRuntimeMediator(after));

            LogAssert.Expect(LogType.Error, new Regex("Throwing"));
            LogAssert.Expect(LogType.Exception, new Regex("Boom"));

            Assert.DoesNotThrow(() => registrar.Cleanup(),
                "One mediator's throwing OnCleanup() must not abort Cleanup() for the whole module.");

            Assert.AreEqual(1, before.OnCleanupCallCount,
                "A sibling mediator must still be cleaned up despite another mediator throwing.");
            Assert.AreEqual(1, after.OnCleanupCallCount,
                "A sibling mediator must still be cleaned up despite another mediator throwing.");
            Assert.AreEqual(0, registrar.RuntimeMediators.Count,
                "The runtime mediator list must still be cleared even though one mediator threw.");
        }
    }
}
