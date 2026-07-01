using NUnit.Framework;
using mvcExpress;
using System;
using System.Reflection;
using System.Threading.Tasks;
using mvcExpress.Internal.Messaging;
using UnityEngine;
using UnityEngine.TestTools;

namespace mvcExpress.Tests
{
    // IMPORTANT: All tests must Dispose() their buses. MvcMessageBus uses static Storage<TMessage>
    // arrays indexed by instance ID. Undisposed buses leave handlers in static memory between tests.
    // Using `using var` or explicit Dispose() in TearDown satisfies this requirement.
    public class MvcMessageBus_UnitTests
    {
        // Private nested types ensure static Storage<T> slots are unique to this test class.
        private struct TestMessage : IMessage { }
        private struct TestMessage2 : IMessage { }
        private struct MultiInterfaceMessage : IMessage, IMessage<int> { }

        [Test]
        public void Subscribe_Publish_HandlerInvoked()
        {
            using var bus = new MvcMessageBus();
            int callCount = 0;

            bus.Subscribe<TestMessage>(() => callCount++);
            bus.Publish<TestMessage>();

            Assert.That(callCount, Is.EqualTo(1), "Handler must be invoked exactly once after Publish.");
        }

        [Test]
        public void Subscribe_Unsubscribe_Publish_HandlerNotInvoked()
        {
            using var bus = new MvcMessageBus();
            int callCount = 0;
            var action = new Action(() => callCount++);

            bus.Subscribe<TestMessage>(action);
            bus.Unsubscribe<TestMessage>(action);
            bus.Publish<TestMessage>();

            Assert.That(callCount, Is.EqualTo(0), "Handler must not be invoked after Unsubscribe.");
        }

        [Test]
        public void Unsubscribe_SameTokenTwice_IsNoOp()
        {
            using var bus = new MvcMessageBus();
            var action = new Action(() => {});
            var token = bus.Subscribe<TestMessage>(action);

            bus.Unsubscribe<TestMessage>(action);
            // Second unsubscribe must not throw
            Assert.DoesNotThrow(() => bus.Unsubscribe<TestMessage>(action));
        }

        [Test]
        public void Publish_ToBusA_DoesNotTriggerBusB()
        {
            using var busA = new MvcMessageBus();
            using var busB = new MvcMessageBus();

            bool busATriggered = false;
            bool busBTriggered = false;

            busA.Subscribe<TestMessage>(() => busATriggered = true);
            busB.Subscribe<TestMessage>(() => busBTriggered = true);

            busA.Publish<TestMessage>();

            Assert.IsTrue(busATriggered, "BusA handler must fire.");
            Assert.IsFalse(busBTriggered, "BusB handler must not fire when BusA publishes.");
        }

        [Test]
        public void InstanceId_RecycledOnDispose_NoStaleHandlers()
        {
            // Practical effect of ID recycling: after disposing a bus and creating a new one,
            // the new bus must NOT see the old bus's subscriptions even if it reuses the same ID.
            int handlerCallCount = 0;
            var busA = new MvcMessageBus();
            busA.Subscribe<TestMessage>(() => handlerCallCount++);
            busA.Dispose(); // clears Storage slot and recycles ID

            // New bus (may reuse busA's ID from the recycle queue)
            using var busB = new MvcMessageBus();
            busB.Publish<TestMessage>(); // must NOT invoke busA's old handler

            Assert.That(handlerCallCount, Is.EqualTo(0),
                "New bus must not fire handlers registered on a disposed bus that shared its instance ID.");
        }

        [Test]
        public void HasSubscribers_FastCheck_ReturnsCorrectState()
        {
            using var bus = new MvcMessageBus();
            Assert.IsFalse(bus.HasSubscribers<TestMessage>(), "Must be false before any subscription.");

            var action = new Action(() => {});
            bus.Subscribe<TestMessage>(action);
            Assert.IsTrue(bus.HasSubscribers<TestMessage>(), "Must be true after subscription.");

            // HasSubscribers checks InstanceCounts, which is a high-watermark counter.
            // Individual Unsubscribe (by delegate or token) nulls the slot but does not
            // decrement the count. UnsubscribeAll is the only path that resets the count
            // to zero, which is the correct way to assert "no subscribers remain."
            bus.UnsubscribeAll<TestMessage>();
            Assert.IsFalse(bus.HasSubscribers<TestMessage>(), "Must be false after UnsubscribeAll.");
        }

        [Test]
        public void DebugStatistics_ReturnsCorrectCounts()
        {
            using var bus = new MvcMessageBus();
            bus.Subscribe<TestMessage>(() => {});
            bus.Subscribe<TestMessage2>(() => {});

            var stats = bus.GetSubscriptionStatistics();
            Assert.That(stats, Is.Not.Null);
            Assert.That(stats.Count, Is.EqualTo(2), "Statistics must reflect both subscribed message types.");

            var (total, active) = bus.GetAggregateSubscriptionStatistics();
            Assert.That(total, Is.EqualTo(2));
            Assert.That(active, Is.EqualTo(2));
        }

        [Test]
        public void Dispose_ClearsHandlers_SubsequentPublishIsNoOp()
        {
            int callCount = 0;
            var bus = new MvcMessageBus();
            bus.Subscribe<TestMessage>(() => callCount++);

            bus.Dispose();

            // Publishing on a disposed bus must not invoke handlers and must not throw
            Assert.DoesNotThrow(() => bus.Publish<TestMessage>());
            Assert.That(callCount, Is.EqualTo(0), "Handlers must not fire after Dispose.");
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Test]
        public void Subscribe_MessageWithMultipleIMessageInterfaces_LogsValidationWarning()
        {
            using var bus = new MvcMessageBus();
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("implements multiple IMessage interfaces"));

            bus.Subscribe<MultiInterfaceMessage>(() => {});
        }

        [Test]
        public void Publish_FromBackgroundThread_ThrowsInvalidOperationException()
        {
            using var bus = new MvcMessageBus();

            var ex = Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => bus.Publish<TestMessage2>()));
            Assert.That(ex.Message, Does.Contain("Messenger.Publish was called from a background thread"));
        }
#endif
    }
}
