using NUnit.Framework;
using mvcExpress;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using mvcExpress.Internal.Messaging;
using UnityEngine;
using UnityEngine.TestTools;

namespace mvcExpress.Tests
{
    // IMPORTANT: All tests must Dispose() their bus. MvcMessageBus uses static generic storage for
    // the single application bus. Disposal clears that storage so handlers cannot leak between tests.
    // Using `using var` or explicit Dispose() in TearDown satisfies this requirement.
    public class MvcMessageBus_UnitTests
    {
        // Private nested types ensure static Storage<T> slots are unique to this test class.
        private struct TestMessage : IMessage { }
        private struct TestMessage2 : IMessage { }
        private struct MultiInterfaceMessage : IMessage, IMessage<int> { }
        private struct Arity12Message : IMessage<int, int, int, int, int, int, int, int, int, int, int, int> { }
        private struct Arity6OnceMessage : IMessage<int, int, int, int, int, int> { }
        private struct Arity12OnceMessage : IMessage<int, int, int, int, int, int, int, int, int, int, int, int> { }
        private struct Arity6WhenMessage : IMessage<int, int, int, int, int, int> { }
        private struct Arity12WhenMessage : IMessage<int, int, int, int, int, int, int, int, int, int, int, int> { }

        // Instance-method target for delegate-equality tests - lets us create two distinct
        // Action instances (`new Action(target.Handle)`) that are target+method equal but not
        // reference equal, matching how callers typically recreate a handler to unsubscribe it.
        private class DelegateTarget
        {
            public int CallCount;
            public void Handle() => CallCount++;
        }

        // Records its name into a shared log on invocation - used to observe relative firing order
        // across multiple distinct subscribers (MSG-03 follow-up: duplicate-registration removal order).
        private class NamedDelegateTarget
        {
            private readonly List<string> _log;
            private readonly string _name;
            public NamedDelegateTarget(List<string> log, string name) { _log = log; _name = name; }
            public void Handle() => _log.Add(_name);
        }

        [Test]
        public void PublicApi_HasNoPublicConstructors()
        {
            var publicConstructors = typeof(MvcMessageBus).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

            Assert.That(publicConstructors, Is.Empty,
                "Application code must obtain the single MessageBus from framework-owned APIs, never construct one.");
        }

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

        /// <summary>
        /// A null handler used to flow through to SubscribeInternal, mutating slot/free-list/count
        /// state before failing (confusingly, and only in Editor/Dev) - Subscribe must now reject it
        /// as the very first statement, before any state changes.
        /// </summary>
        [Test]
        public void Subscribe_NullHandler_ThrowsArgumentNullException()
        {
            using var bus = new MvcMessageBus();
            Assert.Throws<ArgumentNullException>(() => bus.Subscribe<TestMessage>(null));
        }

        /// <summary>
        /// Unsubscribe-by-delegate must match by target+method (ordinary C# `-=` semantics), not by
        /// reference equality - a freshly created delegate instance pointing at the same target and
        /// method must still remove the subscription, even though it is not the same delegate object
        /// used to subscribe.
        /// </summary>
        [Test]
        public void Unsubscribe_ByRecreatedEquivalentDelegate_RemovesSubscription()
        {
            using var bus = new MvcMessageBus();
            var target = new DelegateTarget();

            bus.Subscribe<TestMessage>(new Action(target.Handle));
            Assert.IsTrue(bus.HasSubscribers<TestMessage>(), "Precondition: subscription must be active.");

            // Deliberately a fresh Action instance, not the one used to subscribe.
            bus.Unsubscribe<TestMessage>(new Action(target.Handle));

            Assert.IsFalse(bus.HasSubscribers<TestMessage>(),
                "Unsubscribe must remove the subscription via target+method equality even though a " +
                "different (but equivalent) delegate instance was passed than the one used to subscribe.");

            bus.Publish<TestMessage>();
            Assert.That(target.CallCount, Is.EqualTo(0), "Handler must not fire after being unsubscribed via an equivalent delegate.");
        }

        /// <summary>
        /// MSG-03 follow-up: with duplicate equivalent registrations (the same target+method
        /// subscribed twice), Unsubscribe-by-delegate must remove the LAST-subscribed occurrence,
        /// matching C# event `-=` semantics (<c>Delegate.Remove</c> removes the last matching
        /// occurrence, not the first). Removing the first occurrence instead would leave the
        /// surviving duplicate in the wrong relative position versus other subscribers registered
        /// in between - observable here as "B" firing after "C" instead of before it.
        /// </summary>
        [Test]
        public void Unsubscribe_WithDuplicateEquivalentRegistrations_RemovesLastSubscribedOccurrence_MatchingCSharpEventSemantics()
        {
            using var bus = new MvcMessageBus();
            var order = new List<string>();
            var a = new NamedDelegateTarget(order, "A");
            var b = new NamedDelegateTarget(order, "B");
            var c = new NamedDelegateTarget(order, "C");

            bus.Subscribe<TestMessage>(new Action(a.Handle));
            bus.Subscribe<TestMessage>(new Action(b.Handle)); // B, first occurrence
            bus.Subscribe<TestMessage>(new Action(c.Handle));
            bus.Subscribe<TestMessage>(new Action(b.Handle)); // B, duplicate second occurrence

            bus.Unsubscribe<TestMessage>(new Action(b.Handle));
            bus.Publish<TestMessage>();

            Assert.AreEqual(new[] { "A", "B", "C" }, order.ToArray(),
                "Removing one of two duplicate registrations must remove the LAST-subscribed " +
                "occurrence, leaving the earlier one in its original subscribe-order position " +
                "(before C) - a first-match removal would instead produce [A, C, B].");
        }

        [Test]
        public void Unsubscribe_SameTokenTwice_IsNoOp()
        {
            using var bus = new MvcMessageBus();
            var action = new Action(() => {});
            var token = bus.Subscribe<TestMessage>(action);

            bus.Unsubscribe<TestMessage>(token);
            // Second unsubscribe of the same token must not throw and must be a genuine no-op.
            Assert.DoesNotThrow(() => bus.Unsubscribe<TestMessage>(token));
        }

        [Test]
        public void Unsubscribe_SameTokenTwice_DoesNotCorruptFreeList()
        {
            // COR-15 regression: a double Unsubscribe(token) must not leave the handle free list
            // self-referencing. A corrupted free list hands the SAME handle out to two different
            // later subscribers, silently orphaning the first one's handler (still firing, but no
            // longer reachable via its own token).
            using var bus = new MvcMessageBus();
            var a = new DelegateTarget();
            var b = new DelegateTarget();
            var c = new DelegateTarget();

            var tokenA = bus.Subscribe<TestMessage>(new Action(a.Handle));
            bus.Unsubscribe<TestMessage>(tokenA);
            bus.Unsubscribe<TestMessage>(tokenA); // repeat with the already-consumed token

            var tokenB = bus.Subscribe<TestMessage>(new Action(b.Handle));
            var tokenC = bus.Subscribe<TestMessage>(new Action(c.Handle));

            Assert.AreNotEqual(tokenB.Handle, tokenC.Handle,
                "A corrupted free list (self-referencing after the double-unsubscribe) hands out " +
                "the same handle to two different subscribers.");

            bus.Publish<TestMessage>();
            Assert.AreEqual(1, b.CallCount);
            Assert.AreEqual(1, c.CallCount);

            // Both new subscribers must remain independently removable via their own tokens.
            bus.Unsubscribe<TestMessage>(tokenB);
            bus.Publish<TestMessage>();
            Assert.AreEqual(1, b.CallCount, "B was unsubscribed and must not fire again.");
            Assert.AreEqual(2, c.CallCount, "C must still be live, unaffected by B's removal.");

            bus.Unsubscribe<TestMessage>(tokenC);
            bus.Publish<TestMessage>();
            Assert.AreEqual(1, b.CallCount);
            Assert.AreEqual(2, c.CallCount, "C was unsubscribed and must not fire again.");
        }

        [Test]
        public void Publish_ReachesEveryMatchingSubscriberOnApplicationBus()
        {
            using var bus = new MvcMessageBus();

            var firstSubscriberCount = 0;
            var secondSubscriberCount = 0;

            bus.Subscribe<TestMessage>(() => firstSubscriberCount++);
            bus.Subscribe<TestMessage>(() => secondSubscriberCount++);

            bus.Publish<TestMessage>();

            Assert.That(firstSubscriberCount, Is.EqualTo(1));
            Assert.That(secondSubscriberCount, Is.EqualTo(1));
        }

        [Test]
        public void HasSubscribers_FastCheck_ReturnsCorrectState()
        {
            using var bus = new MvcMessageBus();
            Assert.IsFalse(bus.HasSubscribers<TestMessage>(), "Must be false before any subscription.");

            var action = new Action(() => {});
            bus.Subscribe<TestMessage>(action);
            Assert.IsTrue(bus.HasSubscribers<TestMessage>(), "Must be true after subscription.");

            bus.UnsubscribeAll<TestMessage>();
            Assert.IsFalse(bus.HasSubscribers<TestMessage>(), "Must be false after UnsubscribeAll.");
        }

        /// <summary>
        /// HasSubscribers used to read the slot high-water Count that individual Unsubscribe never
        /// decremented - only UnsubscribeAll reset it - so this returned true "forever" after the
        /// first subscription, even with zero live subscribers. It now reads a live count that both
        /// Unsubscribe overloads decrement immediately, matching the "currently listening" contract
        /// its own XML doc has always advertised.
        /// </summary>
        [Test]
        public void HasSubscribers_AfterIndividualUnsubscribeByToken_ReturnsFalse()
        {
            using var bus = new MvcMessageBus();
            var token = bus.Subscribe<TestMessage>(() => { });
            Assert.IsTrue(bus.HasSubscribers<TestMessage>(), "Precondition: must be true right after subscribing.");

            bus.Unsubscribe<TestMessage>(token);

            Assert.IsFalse(bus.HasSubscribers<TestMessage>(),
                "Must be false immediately after the only subscriber unsubscribes by token - " +
                "not just after a later UnsubscribeAll.");
        }

        [Test]
        public void HasSubscribers_AfterIndividualUnsubscribeByDelegate_ReturnsFalse()
        {
            using var bus = new MvcMessageBus();
            var action = new Action(() => { });
            bus.Subscribe<TestMessage>(action);
            Assert.IsTrue(bus.HasSubscribers<TestMessage>(), "Precondition: must be true right after subscribing.");

            bus.Unsubscribe<TestMessage>(action);

            Assert.IsFalse(bus.HasSubscribers<TestMessage>(),
                "Must be false immediately after the only subscriber unsubscribes by delegate - " +
                "not just after a later UnsubscribeAll.");
        }

        [Test]
        public void HasSubscribers_OneOfTwoUnsubscribes_StaysTrueUntilTheLastOneLeaves()
        {
            using var bus = new MvcMessageBus();
            var actionA = new Action(() => { });
            var actionB = new Action(() => { });
            bus.Subscribe<TestMessage>(actionA);
            bus.Subscribe<TestMessage>(actionB);

            bus.Unsubscribe<TestMessage>(actionA);
            Assert.IsTrue(bus.HasSubscribers<TestMessage>(),
                "One of two subscribers unsubscribing must not clear the live count while the other remains.");

            bus.Unsubscribe<TestMessage>(actionB);
            Assert.IsFalse(bus.HasSubscribers<TestMessage>(),
                "Must go false once the second (last remaining) subscriber also unsubscribes.");
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

        /// <summary>
        /// <c>HasSubscriptionsForType</c> (backing <c>GetAggregateSubscriptionStatistics</c>/
        /// <c>GetSubscriptionStatistics</c>) reads the same live counter as <c>HasSubscribers</c>
        /// via reflection. It had the identical high-water-mark bug and is fixed the same way - this
        /// guards that the debug-statistics surface reflects a live unsubscribe, not just the public
        /// HasSubscribers API tested above.
        /// </summary>
        [Test]
        public void DebugStatistics_AfterOnlySubscriberUnsubscribes_MessageTypeNoLongerActive()
        {
            using var bus = new MvcMessageBus();
            var action = new Action(() => { });
            bus.Subscribe<TestMessage>(action);
            bus.Subscribe<TestMessage2>(() => { }); // stays subscribed - control for "still active"

            bus.Unsubscribe<TestMessage>(action);

            var (total, active) = bus.GetAggregateSubscriptionStatistics();
            Assert.That(total, Is.EqualTo(2), "Both message types were used at least once, so both are still tracked.");
            Assert.That(active, Is.EqualTo(1),
                "Only TestMessage2 should count as active - TestMessage's only subscriber unsubscribed " +
                "individually, which must be reflected immediately, not only after UnsubscribeAll.");
        }

        [Test]
        public void SubscribePublishUnsubscribe_Arity12_WorksLikeLowerArities()
        {
            using var bus = new MvcMessageBus();
            int callCount = 0;
            int lastSum = 0;
            string lastPayloadSequence = null;

            void Handler(int p1, int p2, int p3, int p4, int p5, int p6, int p7, int p8, int p9, int p10, int p11, int p12)
            {
                callCount++;
                lastSum = p1 + p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9 + p10 + p11 + p12;
                lastPayloadSequence = $"{p1}-{p2}-{p3}-{p4}-{p5}-{p6}-{p7}-{p8}-{p9}-{p10}-{p11}-{p12}";
            }

            var token = bus.Subscribe<Arity12Message, int, int, int, int, int, int, int, int, int, int, int, int>(Handler);
            bus.Publish<Arity12Message, int, int, int, int, int, int, int, int, int, int, int, int>(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);

            Assert.That(callCount, Is.EqualTo(1),
                "Arity-12 publish/subscribe should dispatch exactly like every lower arity - " +
                "this is a template-drift smoke test since Params03-12 are mechanically generated " +
                "from the same Params00/01 pattern already covered by other tests in this file.");
            Assert.That(lastSum, Is.EqualTo(78),
                "All twelve payload values must be forwarded to the handler unchanged (1+2+...+12=78).");
            Assert.That(lastPayloadSequence, Is.EqualTo("1-2-3-4-5-6-7-8-9-10-11-12"),
                "Payload values must be forwarded to the handler in the exact positional order they were published - " +
                "the sum check alone cannot detect a parameter-ordering/positional-swap bug because addition is " +
                "commutative (any permutation of 1..12 sums to 78).");

            bus.Unsubscribe<Arity12Message, int, int, int, int, int, int, int, int, int, int, int, int>(token);
            bus.Publish<Arity12Message, int, int, int, int, int, int, int, int, int, int, int, int>(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);

            Assert.That(callCount, Is.EqualTo(1),
                "Unsubscribe-by-token at arity 12 must remove the handler, so a second publish must not invoke it again.");
        }

        /// <summary>
        /// SubscribeOnce was extended from arity 0-5 up to 0-12 to match the base Subscribe/Publish
        /// API (MSG-08). This pins the mechanical pattern at arity 6, the first of the newly-added
        /// arities: the handler must fire exactly once, receive all payload values, then auto-unsubscribe.
        /// </summary>
        [Test]
        public void SubscribeOnce_Arity6_FiresOnceWithCorrectPayloadThenAutoUnsubscribes()
        {
            using var bus = new MvcMessageBus();
            int callCount = 0;
            int lastSum = 0;

            void Handler(int p1, int p2, int p3, int p4, int p5, int p6)
            {
                callCount++;
                lastSum = p1 + p2 + p3 + p4 + p5 + p6;
            }

            bus.SubscribeOnce<Arity6OnceMessage, int, int, int, int, int, int>(Handler);
            bus.Publish<Arity6OnceMessage, int, int, int, int, int, int>(1, 2, 3, 4, 5, 6);

            Assert.That(callCount, Is.EqualTo(1), "SubscribeOnce must invoke the handler on the first publish.");
            Assert.That(lastSum, Is.EqualTo(21), "All six payload values must be forwarded to the handler unchanged (1+2+...+6=21).");

            bus.Publish<Arity6OnceMessage, int, int, int, int, int, int>(10, 20, 30, 40, 50, 60);

            Assert.That(callCount, Is.EqualTo(1),
                "SubscribeOnce must auto-unsubscribe after the first delivery, so a second publish at arity 6 - " +
                "the first of the newly-added arities (6-12) - must not invoke the handler again.");
        }

        /// <summary>
        /// Same as the arity-6 test above, but at arity 12 - the last of the newly-added arities -
        /// to confirm the mechanical pattern still holds at the top of the range.
        /// </summary>
        [Test]
        public void SubscribeOnce_Arity12_FiresOnceWithCorrectPayloadThenAutoUnsubscribes()
        {
            using var bus = new MvcMessageBus();
            int callCount = 0;
            int lastSum = 0;

            void Handler(int p1, int p2, int p3, int p4, int p5, int p6, int p7, int p8, int p9, int p10, int p11, int p12)
            {
                callCount++;
                lastSum = p1 + p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9 + p10 + p11 + p12;
            }

            bus.SubscribeOnce<Arity12OnceMessage, int, int, int, int, int, int, int, int, int, int, int, int>(Handler);
            bus.Publish<Arity12OnceMessage, int, int, int, int, int, int, int, int, int, int, int, int>(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);

            Assert.That(callCount, Is.EqualTo(1), "SubscribeOnce must invoke the handler on the first publish.");
            Assert.That(lastSum, Is.EqualTo(78), "All twelve payload values must be forwarded to the handler unchanged (1+2+...+12=78).");

            bus.Publish<Arity12OnceMessage, int, int, int, int, int, int, int, int, int, int, int, int>(10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120);

            Assert.That(callCount, Is.EqualTo(1),
                "SubscribeOnce must auto-unsubscribe after the first delivery, so a second publish at arity 12 - " +
                "the last of the newly-added arities (6-12) - must not invoke the handler again.");
        }

        /// <summary>
        /// SubscribeWhen was extended from arity 0-5 up to 0-12 to match the base Subscribe/Publish
        /// API (MSG-08). This pins the mechanical pattern at arity 6, the first of the newly-added
        /// arities: the handler must only fire while the condition evaluates to true, and must
        /// receive all payload values when it does.
        /// </summary>
        [Test]
        public void SubscribeWhen_Arity6_OnlyInvokesHandlerWhenConditionTrue()
        {
            using var bus = new MvcMessageBus();
            bool condition = false;
            int callCount = 0;
            int lastSum = 0;

            void Handler(int p1, int p2, int p3, int p4, int p5, int p6)
            {
                callCount++;
                lastSum = p1 + p2 + p3 + p4 + p5 + p6;
            }

            bus.SubscribeWhen<Arity6WhenMessage, int, int, int, int, int, int>(Handler, () => condition);

            bus.Publish<Arity6WhenMessage, int, int, int, int, int, int>(1, 2, 3, 4, 5, 6);
            Assert.That(callCount, Is.EqualTo(0), "SubscribeWhen must not invoke the handler while the condition is false.");

            condition = true;
            bus.Publish<Arity6WhenMessage, int, int, int, int, int, int>(1, 2, 3, 4, 5, 6);

            Assert.That(callCount, Is.EqualTo(1), "SubscribeWhen must invoke the handler once the condition evaluates to true.");
            Assert.That(lastSum, Is.EqualTo(21),
                "All six payload values must be forwarded to the handler unchanged (1+2+...+6=21) at arity 6, " +
                "the first of the newly-added arities (6-12).");
        }

        /// <summary>
        /// Same as the arity-6 test above, but at arity 12 - the last of the newly-added arities -
        /// to confirm the mechanical pattern still holds at the top of the range.
        /// </summary>
        [Test]
        public void SubscribeWhen_Arity12_OnlyInvokesHandlerWhenConditionTrue()
        {
            using var bus = new MvcMessageBus();
            bool condition = false;
            int callCount = 0;
            int lastSum = 0;

            void Handler(int p1, int p2, int p3, int p4, int p5, int p6, int p7, int p8, int p9, int p10, int p11, int p12)
            {
                callCount++;
                lastSum = p1 + p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9 + p10 + p11 + p12;
            }

            bus.SubscribeWhen<Arity12WhenMessage, int, int, int, int, int, int, int, int, int, int, int, int>(Handler, () => condition);

            bus.Publish<Arity12WhenMessage, int, int, int, int, int, int, int, int, int, int, int, int>(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
            Assert.That(callCount, Is.EqualTo(0), "SubscribeWhen must not invoke the handler while the condition is false.");

            condition = true;
            bus.Publish<Arity12WhenMessage, int, int, int, int, int, int, int, int, int, int, int, int>(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);

            Assert.That(callCount, Is.EqualTo(1), "SubscribeWhen must invoke the handler once the condition evaluates to true.");
            Assert.That(lastSum, Is.EqualTo(78),
                "All twelve payload values must be forwarded to the handler unchanged (1+2+...+12=78) at arity 12, " +
                "the last of the newly-added arities (6-12).");
        }

        /// <summary>
        /// Regression test for MSG-01: reentrantly subscribing a new handler from inside a handler
        /// that is itself running during Publish must never fire in that same Publish call - whether
        /// or not a free slot happens to be available from an earlier, unrelated Unsubscribe.
        /// </summary>
        /// <remarks>
        /// Before the original fix, SubscribeInternal's data-slot reuse (an older free-list design)
        /// was completely blind to an in-progress Publish: if a slot freed by an earlier Unsubscribe
        /// sat ahead of the handler currently being invoked (still within the Publish loop's
        /// already-captured <c>count</c>), a reentrant subscription could land there and fire in the
        /// very same Publish - purely as a side effect of invisible free-list history, never any
        /// documented or intended behavior. The storage model has since changed (Unsubscribe now
        /// physically compacts the dense handler array instead of leaving reusable holes, tracked via
        /// a separate handle table - see MvcMessageBus.Params00.cs's header comment), which removes
        /// data-slot reuse from Subscribe entirely: every subscription always appends past whatever
        /// the current publish already captured as <c>count</c>, full stop, regardless of handle or
        /// free-list history. This test is kept as a regression guard for that structural invariant -
        /// handler C is subscribed before handler A specifically so that A's freed slot, once
        /// reclaimed by compaction, would have sat ahead of C's position under the old design, which
        /// is exactly the arrangement that used to expose the bug.
        /// </remarks>
        [Test]
        public void Subscribe_ReentrantDuringPublish_NeverFiresInSamePublishRegardlessOfFreeSlotHistory()
        {
            using var bus = new MvcMessageBus();
            int bCallCount = 0;
            Action bHandler = () => bCallCount++;

            // C is subscribed first (slot 0); A second (slot 1). Unsubscribing A frees slot 1 - a
            // slot index still ahead of C's own slot 0, so the freed slot is still unvisited by the
            // Publish loop when it reaches C.
            Action cHandler = () => bus.Subscribe<TestMessage>(bHandler);
            bus.Subscribe<TestMessage>(cHandler);
            var tokenA = bus.Subscribe<TestMessage>(() => { });
            bus.Unsubscribe<TestMessage>(tokenA);

            // First Publish: C runs and reentrantly subscribes B. Pre-fix, B would reuse A's freed
            // slot (still within this Publish's already-captured handler count) and fire immediately.
            bus.Publish<TestMessage>();

            Assert.That(bCallCount, Is.EqualTo(0),
                "A handler subscribed reentrantly during Publish must never fire during that same " +
                "Publish call, regardless of whether a free slot happens to be available (MSG-01) - " +
                "this would have failed before the fix, since a free slot from A's earlier Unsubscribe " +
                "was available ahead of C's position in the handler array.");

            // Second Publish: B's subscription was deferred, not lost - it must fire now.
            bus.Publish<TestMessage>();

            Assert.That(bCallCount, Is.EqualTo(1),
                "The reentrantly-subscribed handler's subscription must not be lost - it must fire on " +
                "the very next Publish after being deferred.");
        }

        /// <summary>
        /// The core guarantee the handle-indirection storage model exists for. Under the older
        /// design, a <see cref="SubscriptionToken"/> encoded a raw array index directly - so
        /// compacting the array to reclaim a gap (physically moving a live subscriber to a new
        /// position) silently invalidated that subscriber's own, already-issued token. A stable
        /// <c>Handle</c> looked up through an indirection table (fixed up whenever compaction moves
        /// something) is what makes this test pass.
        /// </summary>
        [Test]
        public void Unsubscribe_OfOneSubscriber_DoesNotInvalidateAnotherSubscribersToken()
        {
            using var bus = new MvcMessageBus();
            var bCount = 0;
            var cCount = 0;
            var tokenA = bus.Subscribe<TestMessage>(() => { });
            var tokenB = bus.Subscribe<TestMessage>(() => bCount++);
            bus.Subscribe<TestMessage>(() => cCount++);

            // Unsubscribing A is not inside any Publish call, so it compacts immediately - which
            // may physically move B and/or C to fill the gap A leaves behind.
            bus.Unsubscribe<TestMessage>(tokenA);

            // B's own, never-touched token must still correctly find and remove B, even though its
            // physical storage slot may have just moved.
            bus.Unsubscribe<TestMessage>(tokenB);

            bus.Publish<TestMessage>();

            Assert.That(bCount, Is.EqualTo(0),
                "B's own token must still correctly remove B even though an earlier, unrelated " +
                "Unsubscribe physically moved B's storage slot via compaction.");
            Assert.That(cCount, Is.EqualTo(1),
                "C must be completely unaffected by A's removal and still fire normally.");
        }

        /// <summary>
        /// The SubscribeOnce pattern's bread-and-butter case: a handler unsubscribes itself from
        /// inside its own invocation, mid-Publish. Compaction must not run until the outermost
        /// Publish for this message type fully returns, so a self-removal never disturbs any other
        /// (not-yet-visited) subscriber's position during the same dispatch pass.
        /// </summary>
        [Test]
        public void Publish_HandlerUnsubscribesItselfMidPublish_LaterSubscriberStillFiresThisPass()
        {
            using var bus = new MvcMessageBus();
            var selfCount = 0;
            var laterCount = 0;
            var selfToken = default(SubscriptionToken);
            selfToken = bus.Subscribe<TestMessage>(() =>
            {
                selfCount++;
                bus.Unsubscribe<TestMessage>(selfToken);
            });
            bus.Subscribe<TestMessage>(() => laterCount++);

            bus.Publish<TestMessage>();

            Assert.That(selfCount, Is.EqualTo(1));
            Assert.That(laterCount, Is.EqualTo(1),
                "A subscriber positioned after a self-unsubscribing one must still fire during the " +
                "same Publish call - compaction must be deferred until the outermost Publish returns.");

            bus.Publish<TestMessage>();

            Assert.That(selfCount, Is.EqualTo(1), "The self-unsubscribed handler must never fire again.");
            Assert.That(laterCount, Is.EqualTo(2), "The other subscriber keeps firing normally afterward.");
        }

        /// <summary>
        /// Regression test for the nested-publish failure mode found while designing this storage
        /// model: a handler that unsubscribes itself and then re-publishes the same message type
        /// from within its own body used to scramble the OUTER (suspended) Publish call's remaining,
        /// not-yet-visited positions once the inner call returned - silently skipping a completely
        /// unrelated, still-live subscriber for that outer pass. Deferring compaction for as long as
        /// ANY Publish call for this message type is still on the stack (not just the innermost one)
        /// closes this: nothing physically moves until every nested call has fully unwound.
        /// </summary>
        [Test]
        public void NestedPublish_HandlerUnsubscribesThenRepublishes_OuterCallStillVisitsRemainingSubscribers()
        {
            using var bus = new MvcMessageBus();
            var aCount = 0;
            var cCount = 0;
            var dCount = 0;
            bus.Subscribe<TestMessage>(() => aCount++);
            var selfToken = default(SubscriptionToken);
            selfToken = bus.Subscribe<TestMessage>(() =>
            {
                bus.Unsubscribe<TestMessage>(selfToken);
                bus.Publish<TestMessage>(); // nested publish of the same message type
            });
            bus.Subscribe<TestMessage>(() => cCount++);
            bus.Subscribe<TestMessage>(() => dCount++);

            bus.Publish<TestMessage>(); // outer publish

            // A, C, and D are each live for the entire duration of both the outer publish and the
            // nested inner one, so each must fire exactly twice - once per publish call. Before the
            // fix, C and D would only fire once: the outer call's own iteration, resumed after the
            // inner call physically compacted away the self-unsubscribed handler's slot, would have
            // silently skipped whichever of them the compaction shifted into an already-visited
            // position.
            Assert.That(aCount, Is.EqualTo(2));
            Assert.That(cCount, Is.EqualTo(2),
                "C must fire once for the outer publish and once for the nested inner publish - " +
                "neither call may skip it.");
            Assert.That(dCount, Is.EqualTo(2),
                "D must fire once for the outer publish and once for the nested inner publish - " +
                "neither call may skip it.");
        }

        /// <summary>
        /// The actual problem this storage model was built to solve: under the older free-list
        /// design, Unsubscribe only ever nulled a slot in place and Subscribe never reused it, so a
        /// message type under heavy subscribe/unsubscribe churn (e.g. SubscribeOnce on a frequent
        /// message) grew without bound. Compaction now runs immediately whenever it's safe to do so
        /// (no Publish for this message type currently in progress), so churn should stabilize at a
        /// small size proportional to peak concurrent subscribers, not total-ever-subscribed.
        /// </summary>
        [Test]
        public void SubscribeUnsubscribeChurn_DoesNotGrowStorageUnbounded()
        {
            using var bus = new MvcMessageBus();

            for (int i = 0; i < 50; i++)
            {
                var token = bus.Subscribe<TestMessage>(() => { });
                bus.Unsubscribe<TestMessage>(token);
            }

            var storageType = typeof(MvcMessageBus)
                .GetNestedType("Storage0`1", BindingFlags.NonPublic)
                .MakeGenericType(typeof(TestMessage));
            var handlers = (Array)storageType
                .GetField("Handlers", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);

            Assert.That(handlers.Length, Is.LessThanOrEqualTo(8),
                "50 subscribe/unsubscribe cycles on one message type must not grow the dense array " +
                "past a small bound - each cycle compacts immediately since nothing else was " +
                "publishing, instead of accumulating a permanent hole per cycle.");
        }

        /// <summary>
        /// Regression test for a bug found via external review: Publish used to capture the
        /// Handlers ARRAY OBJECT once, in a local variable, before its loop. If a reentrant
        /// Subscribe during that same Publish call forced storage to grow (array already at
        /// capacity), Subscribe replaces the static Handlers field with a brand new array object -
        /// orphaning Publish's already-captured local reference. Any further reentrant Unsubscribe
        /// during that same Publish call always writes into the CURRENT (new) array, which was
        /// invisible to Publish's stale local one, so a handler unsubscribed mid-publish could
        /// still be wrongly invoked. Fixed by reading the array reference fresh on every loop
        /// iteration instead of caching it - <c>Count</c> itself stays a one-time snapshot
        /// (unrelated fix, MSG-01) so a reentrant *subscribe* still correctly defers to the next
        /// publish regardless.
        /// </summary>
        [Test]
        public void Publish_ReentrantSubscribeTriggersArrayGrowth_ThenReentrantUnsubscribeOfPriorHandler_IsRespectedThisPass()
        {
            using var bus = new MvcMessageBus();
            var bCount = 0;
            var tokenB = default(SubscriptionToken);

            Action aHandler = null;
            aHandler = () =>
            {
                // Forces array growth: storage is exactly at INITIAL_CAPACITY (8) when this runs.
                bus.Subscribe<TestMessage>(() => { });
                // Must be respected for THIS publish pass, not just the next one - even though the
                // array Publish is iterating was just replaced by the growth above.
                bus.Unsubscribe<TestMessage>(tokenB);
            };

            bus.Subscribe<TestMessage>(aHandler); // slot 0
            for (int i = 0; i < 5; i++)
            {
                bus.Subscribe<TestMessage>(() => { }); // slots 1-5, filler
            }
            tokenB = bus.Subscribe<TestMessage>(() => bCount++); // slot 6
            bus.Subscribe<TestMessage>(() => { }); // slot 7 - storage is now exactly full (8/8)

            bus.Publish<TestMessage>();

            Assert.That(bCount, Is.EqualTo(0),
                "B was unsubscribed reentrantly, after a different reentrant Subscribe forced this " +
                "message type's storage to grow, during the same Publish call - B must not be " +
                "invoked, even though the array Publish is iterating was replaced mid-call.");
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

        [Test]
        public void Dispose_ThenCreateFreshTestBus_DoesNotRetainSubscriptions()
        {
            var staleHandlerCallCount = 0;
            var firstBus = new MvcMessageBus();
            firstBus.Subscribe<TestMessage>(() => staleHandlerCallCount++);
            firstBus.Dispose();

            using var freshBus = new MvcMessageBus();
            var freshHandlerCallCount = 0;
            freshBus.Subscribe<TestMessage>(() => freshHandlerCallCount++);
            freshBus.Publish<TestMessage>();

            Assert.That(staleHandlerCallCount, Is.Zero);
            Assert.That(freshHandlerCallCount, Is.EqualTo(1));
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

        /// <summary>
        /// Regression test for MSG-02: in Editor/Development builds, Publish isolates each
        /// subscriber - one throwing subscriber must not prevent a later subscriber from receiving
        /// the same publish. (In release builds this isolation is compiled out entirely for
        /// performance, by design - see MvcMessageBus.cs's class remarks and messaging.md's
        /// "Subscriber exceptions" section - which is why this test lives in the
        /// UNITY_EDITOR/DEVELOPMENT_BUILD region: it exercises exactly the branch that exists only
        /// there, and PlayMode tests always run with UNITY_EDITOR defined.)
        /// </summary>
        [Test]
        public void Publish_SubscriberThrows_LaterSubscriberStillInvoked()
        {
            using var bus = new MvcMessageBus();
            bool laterSubscriberCalled = false;

            LogAssert.Expect(LogType.Error, new Regex("Subscriber '.*' threw on TestMessage:.*boom"));

            bus.Subscribe<TestMessage>(() => throw new Exception("boom"));
            bus.Subscribe<TestMessage>(() => laterSubscriberCalled = true);

            Assert.DoesNotThrow(() => bus.Publish<TestMessage>(),
                "Publish itself must not throw when a subscriber does - the exception is caught and logged per-subscriber.");
            Assert.IsTrue(laterSubscriberCalled,
                "A subscriber after a throwing one must still be invoked in the same Publish call.");
        }

        /// <summary>
        /// Subscribe must be validated main-thread-only exactly like Publish (MSG-05) - it no longer
        /// silently tolerates cross-thread calls behind an inconsistent Editor/Dev-only lock.
        /// </summary>
        [Test]
        public void Subscribe_FromBackgroundThread_ThrowsInvalidOperationException()
        {
            using var bus = new MvcMessageBus();

            var ex = Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => bus.Subscribe<TestMessage2>(() => { })));
            Assert.That(ex.Message, Does.Contain("background thread"));
        }
#endif
    }
}
