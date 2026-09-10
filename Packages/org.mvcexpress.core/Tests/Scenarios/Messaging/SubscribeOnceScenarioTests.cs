using System;
using System.Text.RegularExpressions;
using mvcExpress.Internal.Commands;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests.Scenarios
{
    /// <summary>
    /// Coverage for <see cref="mvcExpress.Actors.MediatorMessengerApi.SubscribeOnce{TMessage}"/> and its
    /// one-payload overload. Both wrap the handler in a delegate that unsubscribes in a finally block,
    /// so the handler fires exactly once and the finally-unsubscribe runs even if the handler throws.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public class SubscribeOnceScenarioTests
    {
        private sealed class NoInitModule : MvcModule
        {
            protected override void Awake() { }
            protected override void OnDestroy() { }
        }

        private readonly struct OnceMessage : IMessage { }
        private readonly struct OncePayloadMessage : IMessage<int> { }
        private readonly struct OnceArity6Message : IMessage<int, int, int, int, int, int> { }

        private sealed class FireOnceMediator : MediatorBehaviour
        {
            public static int HandlerCallCount;
            public static int LastPayload;
            public static int LastArity6Sum;

            public static void Reset()
            {
                HandlerCallCount = 0;
                LastPayload = 0;
                LastArity6Sum = 0;
            }

            protected override void OnInitialized()
            {
                Messenger.SubscribeOnce<OnceMessage>(() => HandlerCallCount++);
                Messenger.SubscribeOnce<OncePayloadMessage, int>(p => { HandlerCallCount++; LastPayload = p; });
                Messenger.SubscribeOnce<OnceArity6Message, int, int, int, int, int, int>((p1, p2, p3, p4, p5, p6) =>
                {
                    HandlerCallCount++;
                    LastArity6Sum = p1 + p2 + p3 + p4 + p5 + p6;
                });
            }
        }

        private sealed class ThrowingOnceMediator : MediatorBehaviour
        {
            public static int HandlerCallCount;

            public static void Reset()
            {
                HandlerCallCount = 0;
            }

            protected override void OnInitialized()
            {
                Messenger.SubscribeOnce<OnceMessage>(() =>
                {
                    HandlerCallCount++;
                    throw new InvalidOperationException("intentional test failure");
                });
            }
        }

        private MvcDiContainer _container;
        private MvcMessageBus _bus;
        private GameObject _moduleGo;
        private NoInitModule _module;

        [SetUp]
        public void SetUp()
        {
            FireOnceMediator.Reset();
            ThrowingOnceMediator.Reset();
            _moduleGo = new GameObject("SubscribeOnceModule");
            _module = _moduleGo.AddComponent<NoInitModule>();
            _container = new MvcDiContainer();
            _bus = new MvcMessageBus();
        }

        [TearDown]
        public void TearDown()
        {
            _bus?.Dispose();
            _container?.Dispose();
            if (_moduleGo != null) Object.DestroyImmediate(_moduleGo);
        }

        [Test]
        public void SubscribeOnce_ZeroPayload_HandlerFiresOnceThenAutoUnsubscribes()
        {
            var mediator = CreateMediator<FireOnceMediator>("FireOnceMediator");

            _bus.Publish<OnceMessage>();
            _bus.Publish<OnceMessage>();

            Assert.That(FireOnceMediator.HandlerCallCount, Is.EqualTo(1),
                "SubscribeOnce must invoke the handler on the first publish and automatically " +
                "unsubscribe, so a second publish of the same message must not invoke it again.");

            Object.DestroyImmediate(mediator.gameObject);
        }

        [Test]
        public void SubscribeOnce_OnePayload_HandlerReceivesPayloadAndFiresOnce()
        {
            var mediator = CreateMediator<FireOnceMediator>("FireOnceMediator");

            _bus.Publish<OncePayloadMessage, int>(42);
            _bus.Publish<OncePayloadMessage, int>(99);

            Assert.That(FireOnceMediator.LastPayload, Is.EqualTo(42),
                "The one-time handler must receive the payload from the first publish only.");

            Object.DestroyImmediate(mediator.gameObject);
        }

        /// <summary>
        /// SubscribeOnce was extended from arity 0-5 up to 0-12 to match the base Subscribe/Publish
        /// API (MSG-08). This confirms the mediator-level wrapper (auto-unsubscribe, subscription
        /// tracking) also holds at one of the newly-added arities, not just at the mechanically
        /// identical bus-level overload covered in MvcMessageBus_UnitTests.
        /// </summary>
        [Test]
        public void SubscribeOnce_ArityHigh_HandlerReceivesPayloadAndFiresOnce()
        {
            var mediator = CreateMediator<FireOnceMediator>("FireOnceMediator");

            _bus.Publish<OnceArity6Message, int, int, int, int, int, int>(1, 2, 3, 4, 5, 6);
            _bus.Publish<OnceArity6Message, int, int, int, int, int, int>(10, 20, 30, 40, 50, 60);

            Assert.That(FireOnceMediator.LastArity6Sum, Is.EqualTo(21),
                "The one-time handler must receive the payload from the first publish only, proving " +
                "SubscribeOnce's auto-unsubscribe behavior holds at the newly-added higher arities (6-12), not just 0-5.");

            Object.DestroyImmediate(mediator.gameObject);
        }

        [Test]
        public void SubscribeOnce_HandlerThrows_StillUnsubscribesViaFinally()
        {
            var mediator = CreateMediator<ThrowingOnceMediator>("ThrowingOnceMediator");

            // In Editor/Development builds, Publish now catches each subscriber's exception
            // individually and logs it (MSG-02 - see MvcMessageBus.cs's class remarks), so it no
            // longer propagates out of Publish itself in this build configuration. SubscribeOnce's
            // own wrapper still wraps the *user* handler in its own try/finally specifically to
            // guarantee the unsubscribe runs regardless of whether/where the exception is ultimately
            // caught - this test still pins that finally-based auto-unsubscribe, just observed via
            // the logged error instead of a propagated exception.
            LogAssert.Expect(LogType.Error, new Regex("Subscriber '.*' threw on OnceMessage:.*intentional test failure"));
            Assert.DoesNotThrow(() => _bus.Publish<OnceMessage>());
            Assert.That(ThrowingOnceMediator.HandlerCallCount, Is.EqualTo(1),
                "Precondition: the throwing handler must have run exactly once.");

            // If the finally-unsubscribe did not run, this second publish would invoke (and log
            // from) the handler again.
            Assert.DoesNotThrow(() => _bus.Publish<OnceMessage>(),
                "SubscribeOnce must unsubscribe in a finally block even when the handler throws, " +
                "so the wrapper delegate is not still subscribed for a second publish.");
            Assert.That(ThrowingOnceMediator.HandlerCallCount, Is.EqualTo(1),
                "The handler must not run again on the second publish - proving auto-unsubscribe " +
                "happened despite the first call throwing.");

            Object.DestroyImmediate(mediator.gameObject);
        }

        private T CreateMediator<T>(string name) where T : MediatorBehaviour
        {
            var go = new GameObject(name);
            go.transform.SetParent(_moduleGo.transform);
            var mediator = go.AddComponent<T>();
            mediator.Initialize(_module, _container, _bus);
            return mediator;
        }
    }
}
