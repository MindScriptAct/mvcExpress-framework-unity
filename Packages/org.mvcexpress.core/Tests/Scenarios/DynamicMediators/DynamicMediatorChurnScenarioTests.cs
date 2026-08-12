using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests.Scenarios
{
    /// <summary>
    /// "Popup storm" scenario: attach and detach a dynamic mediator repeatedly, each instance
    /// subscribing to three message types and manually unsubscribing one by token before detach.
    /// Guards against SubscriptionTracker token collisions across message types (H3) and
    /// unbounded handler-array growth under subscribe/unsubscribe churn (H4).
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public class DynamicMediatorChurnScenarioTests
    {
        private const int ChurnIterations = 200;

        private sealed class NoInitModule : MvcModule
        {
            protected override void Awake() { }
            protected override void OnDestroy() { }
        }

        private readonly struct MessageA : IMessage { }
        private readonly struct MessageB : IMessage { }
        private readonly struct MessageC : IMessage { }

        private sealed class PopupMediator : MediatorBehaviour
        {
            public static int MessageACount;
            public static int MessageBCount;
            public static int MessageCCount;

            public static void ResetCounts()
            {
                MessageACount = 0;
                MessageBCount = 0;
                MessageCCount = 0;
            }

            protected override void OnInitialized()
            {
                var tokenA = Messenger.Subscribe<MessageA>(() => MessageACount++);
                Messenger.Subscribe<MessageB>(() => MessageBCount++);
                Messenger.Subscribe<MessageC>(() => MessageCCount++);

                // Manually unsubscribe MessageA before detach - both A and B are each the
                // first subscriber for their type at this point in a fresh bus, so their
                // tokens collide under the pre-H3-fix bug.
                Messenger.Unsubscribe<MessageA>(tokenA);
            }
        }

        private MvcDiContainer _container;
        private MvcMessageBus _bus;
        private GameObject _moduleGo;
        private NoInitModule _module;

        [SetUp]
        public void SetUp()
        {
            PopupMediator.ResetCounts();
            _moduleGo = new GameObject("DynamicMediatorChurnModule");
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
        public void PopupStorm_AttachDetach200Times_NoCrossTypeUntrackCollisionAndNoUnboundedGrowth()
        {
            for (int i = 0; i < ChurnIterations; i++)
            {
                var go = new GameObject("Popup_" + i);
                go.transform.SetParent(_moduleGo.transform);
                var mediator = go.AddComponent<PopupMediator>();
                mediator.Initialize(_module, _container, _bus);

                Object.DestroyImmediate(go);

                if (i == 0)
                {
                    // Checkpoint immediately after the very first iteration, while the
                    // collision-prone tokens (Index=0, Version=2 for A/B/C, each type's first
                    // ever subscriber on this fresh bus) are freshest. On every later iteration
                    // the three message types stay in lockstep - each iteration subscribes and
                    // then fully unsubscribes exactly one slot per type - so the free list
                    // recycles the same slot 0 for A, B, and C every time, and their independent
                    // per-type version counters advance in lockstep. That reproduces the same
                    // colliding token shape on every iteration, but asserting right after
                    // iteration 0 pins the check to the moment the brief's collision scenario
                    // (both A and B first-subscribers, token (0,2)) is guaranteed to have just
                    // occurred, rather than relying only on cumulative state after 200 rounds.
                    PopupMediator.ResetCounts();
                    _bus.Publish<MessageA>();
                    _bus.Publish<MessageB>();
                    _bus.Publish<MessageC>();

                    Assert.That(PopupMediator.MessageACount, Is.EqualTo(0),
                        "Iteration 0 checkpoint: MessageA was manually unsubscribed before detach - " +
                        "publishing it must invoke zero handlers.");
                    Assert.That(PopupMediator.MessageBCount, Is.EqualTo(0),
                        "Iteration 0 checkpoint: MessageB and MessageA were both first-subscribers on " +
                        "this fresh bus, so both received token (Index=0, Version=2). Before the H3 fix, " +
                        "Untrack(tokenA) could remove MessageB's tracked entry instead (token collision " +
                        "across message types), leaving MessageB's bus subscription untracked so " +
                        "UnsubscribeAll on mediator destroy would miss it and this handler would still fire.");
                    Assert.That(PopupMediator.MessageCCount, Is.EqualTo(0),
                        "Iteration 0 checkpoint: MessageC must also be cleaned up automatically on " +
                        "mediator destroy, with no interference from the MessageA/MessageB unsubscribe pattern.");

                    PopupMediator.ResetCounts();
                }
            }

            PopupMediator.ResetCounts();
            _bus.Publish<MessageA>();
            _bus.Publish<MessageB>();
            _bus.Publish<MessageC>();

            Assert.That(PopupMediator.MessageACount, Is.EqualTo(0),
                "MessageA was manually unsubscribed by every mediator instance before detach - " +
                "publishing it after the storm must invoke zero handlers.");
            Assert.That(PopupMediator.MessageBCount, Is.EqualTo(0),
                "MessageB relies on automatic cleanup via SubscriptionTracker.UnsubscribeAll when the " +
                "mediator is destroyed. Before the H3 fix, Untrack(tokenA) could remove MessageB's " +
                "tracked entry instead (token collision across message types), leaving MessageB's bus " +
                "subscription untracked - so UnsubscribeAll would miss it and this handler would still fire.");
            Assert.That(PopupMediator.MessageCCount, Is.EqualTo(0),
                "MessageC must also be cleaned up automatically on mediator destroy, with no interference " +
                "from the MessageA/MessageB unsubscribe pattern.");
        }
    }
}
