using System;
using NUnit.Framework;
using mvcExpress.Internal.Messaging;

namespace mvcExpress.Tests
{
    public class SubscriptionTracker_UnitTests
    {
        private class DummySubscriber
        {
            public int Value;
        }

        private SubscriptionTracker tracker;

        [SetUp]
        public void SetUp()
        {
            tracker = new SubscriptionTracker();
        }

        [Test]
        public void Track_ValidSubscriber_IncrementsCount()
        {
            var subscriber = new DummySubscriber();
            var token = default(SubscriptionToken);

            tracker.Track(typeof(object), subscriber, token, 0, t => { });

            Assert.AreEqual(1, tracker.Count);
        }

        [Test]
        public void Track_NullSubscriber_DoesNotIncrementCount()
        {
            var token = default(SubscriptionToken);

            tracker.Track(typeof(object), null, token, 0, t => { });

            Assert.AreEqual(0, tracker.Count);
        }

        [Test]
        public void Untrack_ExistingToken_DecrementsCountAndReturnsTrue()
        {
            var subscriber = new DummySubscriber();
            var token = default(SubscriptionToken);
            tracker.Track(typeof(object), subscriber, token, 0, t => { });

            bool result = tracker.Untrack(typeof(object), token);

            Assert.IsTrue(result);
            Assert.AreEqual(0, tracker.Count);
        }

        [Test]
        public void Untrack_NonExistentToken_ReturnsFalse()
        {
            var token = default(SubscriptionToken);

            bool result = tracker.Untrack(typeof(object), token);

            Assert.IsFalse(result);
            Assert.AreEqual(0, tracker.Count);
        }

        [Test]
        public void UnsubscribeAll_ValidSubscriptions_InvokesAllAndClears()
        {
            var subscriber = new DummySubscriber();
            var token1 = default(SubscriptionToken);
            var token2 = default(SubscriptionToken);
            bool action1Called = false;
            bool action2Called = false;

            tracker.Track(typeof(object), subscriber, token1, 0, t => action1Called = true);
            tracker.Track(typeof(object), subscriber, token2, 0, t => action2Called = true);

            tracker.UnsubscribeAll();

            Assert.AreEqual(0, tracker.Count);
            Assert.IsTrue(action1Called);
            Assert.IsTrue(action2Called);
        }

        [Test]
        public void UnsubscribeAll_ExceptionInAction_ContinuesAndClears()
        {
            var subscriber = new DummySubscriber();
            var token1 = default(SubscriptionToken);
            var token2 = default(SubscriptionToken);
            bool action2Called = false;

            tracker.Track(typeof(object), subscriber, token1, 0, t => throw new Exception("Test Exception"));
            tracker.Track(typeof(object), subscriber, token2, 0, t => action2Called = true);

            tracker.UnsubscribeAll();

            Assert.AreEqual(0, tracker.Count);
            Assert.IsTrue(action2Called);
        }
    }
}
