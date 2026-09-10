using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests.Scenarios
{
    /// <summary>
    /// Coverage for <see cref="mvcExpress.Actors.MediatorMessengerApi.SubscribeWhen{TMessage}"/>.
    /// The handler is only invoked when the supplied condition evaluates to true, and the condition
    /// is re-checked on every publish rather than being cached at subscribe time.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public class SubscribeWhenScenarioTests
    {
        private sealed class NoInitModule : MvcModule
        {
            protected override void Awake() { }
            protected override void OnDestroy() { }
        }

        private readonly struct GatedMessage : IMessage { }
        private readonly struct GatedArity6Message : IMessage<int, int, int, int, int, int> { }

        private sealed class GatedMediator : MediatorBehaviour
        {
            public static bool ConditionValue;
            public static int HandlerCallCount;
            public static int LastArity6Sum;

            public static void Reset()
            {
                ConditionValue = false;
                HandlerCallCount = 0;
                LastArity6Sum = -1;
            }

            protected override void OnInitialized()
            {
                Messenger.SubscribeWhen<GatedMessage>(() => HandlerCallCount++, () => ConditionValue);
                Messenger.SubscribeWhen<GatedArity6Message, int, int, int, int, int, int>((p1, p2, p3, p4, p5, p6) =>
                {
                    HandlerCallCount++;
                    LastArity6Sum = p1 + p2 + p3 + p4 + p5 + p6;
                }, () => ConditionValue);
            }
        }

        private MvcDiContainer _container;
        private MvcMessageBus _bus;
        private GameObject _moduleGo;
        private NoInitModule _module;

        [SetUp]
        public void SetUp()
        {
            GatedMediator.Reset();
            _moduleGo = new GameObject("SubscribeWhenModule");
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
        public void SubscribeWhen_ConditionFalse_HandlerDoesNotRun()
        {
            var mediator = CreateMediator();
            GatedMediator.ConditionValue = false;

            _bus.Publish<GatedMessage>();

            Assert.That(GatedMediator.HandlerCallCount, Is.EqualTo(0),
                "SubscribeWhen must not invoke the handler when the condition evaluates to false at publish time.");

            Object.DestroyImmediate(mediator.gameObject);
        }

        [Test]
        public void SubscribeWhen_ConditionTrue_HandlerRuns()
        {
            var mediator = CreateMediator();
            GatedMediator.ConditionValue = true;

            _bus.Publish<GatedMessage>();

            Assert.That(GatedMediator.HandlerCallCount, Is.EqualTo(1),
                "SubscribeWhen must invoke the handler when the condition evaluates to true at publish time.");

            Object.DestroyImmediate(mediator.gameObject);
        }

        [Test]
        public void SubscribeWhen_ConditionCheckedPerPublish_NotJustOnce()
        {
            var mediator = CreateMediator();

            GatedMediator.ConditionValue = false;
            _bus.Publish<GatedMessage>();

            GatedMediator.ConditionValue = true;
            _bus.Publish<GatedMessage>();

            GatedMediator.ConditionValue = false;
            _bus.Publish<GatedMessage>();

            Assert.That(GatedMediator.HandlerCallCount, Is.EqualTo(1),
                "The condition must be re-evaluated on every publish (not cached at subscribe time): " +
                "only the middle publish, where the condition was true, should have invoked the handler.");

            Object.DestroyImmediate(mediator.gameObject);
        }

        /// <summary>
        /// SubscribeWhen was extended from arity 0-5 up to 0-12 to match the base Subscribe/Publish
        /// API (MSG-08). This confirms the mediator-level wrapper (condition re-check, subscription
        /// tracking) also holds at one of the newly-added arities, not just at the mechanically
        /// identical bus-level overload covered in MvcMessageBus_UnitTests.
        /// </summary>
        [Test]
        public void SubscribeWhen_ArityHigh_OnlyInvokesHandlerWhenConditionTrue()
        {
            var mediator = CreateMediator();

            GatedMediator.ConditionValue = false;
            _bus.Publish<GatedArity6Message, int, int, int, int, int, int>(1, 2, 3, 4, 5, 6);

            Assert.That(GatedMediator.LastArity6Sum, Is.EqualTo(-1),
                "SubscribeWhen must not invoke the handler at the newly-added higher arities either, when the condition is false.");

            GatedMediator.ConditionValue = true;
            _bus.Publish<GatedArity6Message, int, int, int, int, int, int>(1, 2, 3, 4, 5, 6);

            Assert.That(GatedMediator.LastArity6Sum, Is.EqualTo(21),
                "SubscribeWhen must invoke the handler and forward payload values correctly at the newly-added " +
                "higher arities (6-12), not just 0-5, once the condition evaluates to true.");

            Object.DestroyImmediate(mediator.gameObject);
        }

        private GatedMediator CreateMediator()
        {
            var go = new GameObject("GatedMediator");
            go.transform.SetParent(_moduleGo.transform);
            var mediator = go.AddComponent<GatedMediator>();
            mediator.Initialize(_module, _container, _bus);
            return mediator;
        }
    }
}
