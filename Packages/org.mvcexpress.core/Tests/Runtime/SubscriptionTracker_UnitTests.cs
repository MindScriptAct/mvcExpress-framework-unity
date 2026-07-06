using System;
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
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

        [UnityTest]
        public IEnumerator CleanupDead_WithDeadReferences_RemovesThemAndInvokesAction()
        {
            var token = default(SubscriptionToken);
            bool actionCalled = false;

            TrackDeadSubscriber(tracker, token, t => actionCalled = true);

            // Force GC across frame boundaries. yield return null gives up the main thread
            // so Unity's incremental GC can run between frames - required in Play Mode.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            yield return null;
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            yield return null;

            tracker.CleanupDead();

            Assert.AreEqual(0, tracker.Count);
            Assert.IsTrue(actionCalled);
        }
        
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void TrackDeadSubscriber(SubscriptionTracker tracker, SubscriptionToken token, Action<SubscriptionToken> action)
        {
            var subscriber = new DummySubscriber();
            tracker.Track(typeof(object), subscriber, token, 0, action);
        }

        [UnityTest]
        public IEnumerator CleanupDead_ActionThrowsException_CatchesAndContinues()
        {
            var token1 = default(SubscriptionToken);
            var token2 = default(SubscriptionToken);
            bool action2Called = false;

            TrackDeadSubscriber(tracker, token1, t => throw new Exception("Test Exception"));
            TrackDeadSubscriber(tracker, token2, t => action2Called = true);

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            yield return null;
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            yield return null;

            LogAssert.Expect(LogType.Warning, new Regex("Error unsubscribing tracked subscription: Test Exception"));
            tracker.CleanupDead();

            Assert.AreEqual(0, tracker.Count);
            Assert.IsTrue(action2Called);
        }

        [Test]
        public void CheckAndCleanup_BelowThreshold_DoesNotTriggerCleanup()
        {
            var token = default(SubscriptionToken);
            bool actionCalled = false;
            TrackDeadSubscriber(tracker, token, t => actionCalled = true);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect(); // second pass collects objects promoted to finalization queue

            // Track() itself now calls CheckAndCleanup() once (see L10), so only 18 more calls
            // are needed to stay one below the threshold of 20.
            for (int i = 0; i < 18; i++)
            {
                tracker.CheckAndCleanup();
            }

            Assert.AreEqual(1, tracker.Count);
            Assert.IsFalse(actionCalled);
        }

        [UnityTest]
        public IEnumerator CheckAndCleanup_HitsThreshold_TriggersCleanup()
        {
            var token = default(SubscriptionToken);
            bool actionCalled = false;
            TrackDeadSubscriber(tracker, token, t => actionCalled = true);

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            yield return null;
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            yield return null;

            // Track() itself now calls CheckAndCleanup() once (see L10), so only 19 more calls
            // are needed to land exactly on the threshold of 20.
            for (int i = 0; i < 19; i++)
            {
                tracker.CheckAndCleanup();
            }

            Assert.AreEqual(0, tracker.Count);
            Assert.IsTrue(actionCalled);
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
