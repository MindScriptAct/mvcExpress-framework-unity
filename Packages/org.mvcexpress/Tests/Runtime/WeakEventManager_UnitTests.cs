using System;
using NUnit.Framework;
using mvcExpress.Internal.Utilities;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.TestTools;

namespace mvcExpress.Tests
{
    public class WeakEventManager_UnitTests
    {
        private class DummySubscriber
        {
            public int InvokeCount = 0;
            public void OnEvent(EventArgs args)
            {
                InvokeCount++;
            }
        }

        private WeakEventManager<EventArgs> manager;

        [SetUp]
        public void SetUp()
        {
            manager = new WeakEventManager<EventArgs>();
        }

        [Test]
        public void Subscribe_ValidHandler_IncrementsAliveCount()
        {
            var subscriber = new DummySubscriber();
            manager.Subscribe(subscriber.OnEvent);

            Assert.AreEqual(1, manager.AliveCount);
        }

        [Test]
        public void Subscribe_NullHandler_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => manager.Subscribe(null));
        }

        [Test]
        public void Unsubscribe_ExistingHandler_DecrementsAliveCount()
        {
            var subscriber = new DummySubscriber();
            Action<EventArgs> handler = subscriber.OnEvent; // store delegate so both calls use the same instance
            manager.Subscribe(handler);
            manager.Unsubscribe(handler);

            Assert.AreEqual(0, manager.AliveCount);
        }

        [Test]
        public void Unsubscribe_NonExistentHandler_DoesNothing()
        {
            var subscriber = new DummySubscriber();
            manager.Unsubscribe(subscriber.OnEvent);

            Assert.AreEqual(0, manager.AliveCount);
        }

        [Test]
        public void Unsubscribe_NullHandler_IsNoOp()
        {
            Assert.DoesNotThrow(() => manager.Unsubscribe(null),
                "Unsubscribe(null) must be silently ignored.");
        }

        [Test]
        public void Raise_HappyPath_InvokesAllLivingHandlers()
        {
            var subscriber1 = new DummySubscriber();
            var subscriber2 = new DummySubscriber();

            manager.Subscribe(subscriber1.OnEvent);
            manager.Subscribe(subscriber2.OnEvent);

            manager.Raise(EventArgs.Empty);

            Assert.AreEqual(1, subscriber1.InvokeCount);
            Assert.AreEqual(1, subscriber2.InvokeCount);
        }

        [Test]
        public void Raise_DeadSubscriber_SkipsAndUpdatesDeadCount()
        {
            SubscribeDeadHandler();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect(); // second pass collects objects promoted to finalization queue

            Assert.DoesNotThrow(() => manager.Raise(EventArgs.Empty));
        }

        [Test]
        public void Raise_Reentrancy_QueuesAndInvokesAll()
        {
            var args = EventArgs.Empty;
            int callCount = 0;
            manager.Subscribe(a => 
            {
                callCount++;
                if (callCount == 1)
                {
                    manager.Raise(a);
                }
            });

            manager.Raise(args);

            Assert.AreEqual(2, callCount);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SubscribeDeadHandler()
        {
            var subscriber = new DummySubscriber();
            manager.Subscribe(subscriber.OnEvent);
        }

        [Test]
        public void Cleanup_RemovesDeadDelegates()
        {
            SubscribeDeadHandler();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect(); // second pass collects objects promoted to finalization queue

            manager.Cleanup();

            Assert.AreEqual(0, manager.AliveCount);
        }

        [Test]
        public void Raise_HandlerThrowsException_ContinuesWithRemainingHandlers()
        {
            int survivorCount = 0;
            manager.Subscribe(_ => throw new InvalidOperationException("Deliberate failure"));
            manager.Subscribe(_ => survivorCount++);

            LogAssert.Expect(LogType.Error, new Regex("Error in weak event handler: Deliberate failure"));

            Assert.DoesNotThrow(() => manager.Raise(EventArgs.Empty),
                "Raise must not propagate handler exceptions.");
            Assert.AreEqual(1, survivorCount,
                "Handler after the throwing one must still be invoked.");
        }

        [Test]
        public void Clear_RemovesAllSubscribers()
        {
            var subscriber = new DummySubscriber();
            manager.Subscribe(subscriber.OnEvent);

            manager.Clear();

            Assert.AreEqual(0, manager.AliveCount);
        }
    }
}
