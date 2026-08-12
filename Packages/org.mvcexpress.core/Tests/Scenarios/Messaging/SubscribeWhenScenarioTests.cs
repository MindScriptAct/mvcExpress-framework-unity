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

        private sealed class GatedMediator : MediatorBehaviour
        {
            public static bool ConditionValue;
            public static int HandlerCallCount;

            public static void Reset()
            {
                ConditionValue = false;
                HandlerCallCount = 0;
            }

            protected override void OnInitialized()
            {
                Messenger.SubscribeWhen<GatedMessage>(() => HandlerCallCount++, () => ConditionValue);
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
