using System;
using System.Collections.Generic;
using mvcExpress.Internal.DependencyInjection;
using NUnit.Framework;

namespace mvcExpress.Tests
{
    // Regression coverage: a list-only member (registered via
    // .ToLogicList<T>()/.ToViewList<T>() with no direct key) had no valid dynamic ownership path -
    // it couldn't be individually unregistered, Clear() never disposed it, a member that also had
    // a direct key could be disposed while still sitting in the list, list lifetime was tracked
    // per-list instead of per-member, membership tracking used value equality, and list-binding
    // commit wasn't atomic with the direct-key commit in the same chain.
    [TestFixture]
    public class MvcDiContainer_ListBindingOwnership_UnitTests
    {
        private interface IThing { }

        private sealed class TrackedDisposable : IThing, IDisposable
        {
            public int DisposeCount { get; private set; }
            public void Dispose() => DisposeCount++;
        }

        // Throws if Dispose() runs more than once - proves "disposed exactly once", not just
        // "disposed at least once".
        private sealed class NonIdempotentDisposable : IThing, IDisposable
        {
            public int DisposeCount { get; private set; }
            public void Dispose()
            {
                DisposeCount++;
                if (DisposeCount > 1)
                    throw new InvalidOperationException("Dispose() called more than once.");
            }
        }

        // Overrides value equality so two distinct instances with the same Key compare equal -
        // the exact shape the audit's "distinct value-equal members" bullet describes.
        private sealed class ValueEqualThing : IThing
        {
            public readonly int Key;
            public ValueEqualThing(int key) => Key = key;
            public override bool Equals(object obj) => obj is ValueEqualThing other && other.Key == Key;
            public override int GetHashCode() => Key;
        }

        private MvcDiContainer _container;

        [SetUp]
        public void SetUp()
        {
            _container = new MvcDiContainer();
        }

        [TearDown]
        public void TearDown()
        {
            _container?.Dispose();
        }

        [Test]
        public void UnregisterInstance_TransientListMember_RemovesFromListAndDisposes()
        {
            // A second member keeps the list registration itself alive after `member` is
            // removed, so this test isolates "removed from the list" from the separate (and
            // separately tested) "whole list torn down once empty" behavior.
            var member = new TrackedDisposable();
            var keptMember = new TrackedDisposable();
            _container.Register(member).ToLogicList<IThing>().AsTransient();
            _container.Register(keptMember).ToLogicList<IThing>().AsPermanent();

            _container.UnregisterInstance(member);

            Assert.That(member.DisposeCount, Is.EqualTo(1),
                "UnregisterInstance must dispose a transient list-only member exactly once.");
            var list = _container.Resolve<List<IThing>>();
            CollectionAssert.DoesNotContain(list, member,
                "UnregisterInstance must remove the member from the underlying List<T>.");
            CollectionAssert.Contains(list, keptMember,
                "Removing one member must not disturb another member still in the same list.");
        }

        [Test]
        public void Clear_ListOnlyMember_IsDisposedExactlyOnce()
        {
            var member = new NonIdempotentDisposable();
            _container.Register(member).ToLogicList<IThing>().AsPermanent();

            _container.Clear();

            Assert.That(member.DisposeCount, Is.EqualTo(1),
                "Clear() must dispose a list-only member (never given a direct key) exactly once.");
        }

        [Test]
        public void UnregisterDirectKey_MemberAlsoInList_SurvivesUntilListMembershipAlsoRemoved()
        {
            var member = new TrackedDisposable();
            _container.Register(member)
                .ToLogic()
                .ToLogicList<IThing>()
                .AsTransient();

            _container.Unregister<TrackedDisposable>();

            Assert.That(member.DisposeCount, Is.EqualTo(0),
                "Removing the direct key must not dispose the instance while its list membership " +
                "(a separate reference) is still live.");
            var list = _container.Resolve<List<IThing>>();
            CollectionAssert.Contains(list, member,
                "The instance must remain in the list - and undisposed - after only its direct " +
                "key was unregistered.");

            _container.UnregisterInstance(member);

            Assert.That(member.DisposeCount, Is.EqualTo(1),
                "Once the list membership (the last remaining reference) is also removed, the " +
                "instance must be disposed exactly once.");
        }

        [Test]
        public void UnregisterInstance_PermanentListMember_ThrowsAndLeavesItInPlace()
        {
            var member = new TrackedDisposable();
            _container.Register(member).ToLogicList<IThing>().AsPermanent();

            Assert.Throws<InvalidOperationException>(() => _container.UnregisterInstance(member),
                "A permanent list member must not be individually removable, matching the " +
                "existing guard for permanent direct registrations.");

            Assert.That(member.DisposeCount, Is.EqualTo(0));
            var list = _container.Resolve<List<IThing>>();
            CollectionAssert.Contains(list, member,
                "The permanent member must remain in the list after the rejected unregister attempt.");
        }

        [Test]
        public void TwoValueEqualMembers_BothTrackedAndIndependentlyRemovable()
        {
            var first = new ValueEqualThing(1);
            var second = new ValueEqualThing(1); // distinct instance, but Equals()/GetHashCode() collide
            Assert.That(first, Is.EqualTo(second), "Precondition: the two instances must be value-equal.");
            Assert.That(ReferenceEquals(first, second), Is.False, "Precondition: distinct instances.");

            _container.Register(first).ToLogicList<IThing>().AsTransient();
            _container.Register(second).ToLogicList<IThing>().AsTransient();

            var list = _container.Resolve<List<IThing>>();
            Assert.That(list.Count, Is.EqualTo(2),
                "Both value-equal instances must have been appended to the list.");

            _container.UnregisterInstance(first);

            Assert.That(list.Count, Is.EqualTo(1),
                "Removing one of two value-equal members must remove exactly that instance, not " +
                "collapse bookkeeping for both into one entry.");
            CollectionAssert.Contains(list, second,
                "The second, still-registered value-equal instance must remain in the list.");
        }

        [Test]
        public void RegisterChain_ListBindingFailsAfterDirectKeyPhase_RollsBackDirectKeyToo()
        {
            // Occupy the IReadOnlyList<IThing> alias with a different, unrelated instance first, so
            // the list-binding phase of a later chain is guaranteed to fail when it tries to expose
            // ITS OWN new List<IThing> under that same alias - the "IReadOnlyListAlias"
            // guard. (Registering something genuinely incompatible with List<IThing> here - e.g. a
            // bare `object` - is no longer possible: RegistrationBuilder now validates
            // assignability at the .ToLogicAs<T>() call itself, so the occupant must be a real
            // IReadOnlyList<IThing> - a second List<IThing> instance still triggers the same
            // "different instance" conflict without needing an incompatible type.)
            _container.Register(new List<IThing>()).ToLogicAs<IReadOnlyList<IThing>>().AsPermanent();

            var obj = new TrackedDisposable();
            Assert.Throws<InvalidOperationException>(() =>
                _container.Register(obj).ToLogic().ToLogicList<IThing>().AsPermanent(),
                "The list-binding phase must fail because IReadOnlyList<IThing> already resolves to " +
                "a different instance.");

            Assert.Throws<InvalidOperationException>(() => _container.Resolve<TrackedDisposable>(),
                "The direct key committed earlier in the SAME failed chain must be rolled back too - " +
                "a failed registration chain must be all-or-nothing.");
        }

        [Test]
        public void ToLogicList_AutomaticallyExposesIReadOnlyListAlias()
        {
            var member = new TrackedDisposable();
            _container.Register(member).ToLogicList<IThing>().AsPermanent();

            var mutable = _container.Resolve<List<IThing>>();
            var readOnly = _container.Resolve<IReadOnlyList<IThing>>();

            Assert.That(readOnly, Is.SameAs(mutable),
                "ToLogicList<T>() must automatically expose the same List<T> instance under " +
                "IReadOnlyList<T> at zero extra cost - no copy, no wrapper.");
        }

        [Test]
        public void IReadOnlyListAlias_RemovedWhenLastMemberRemoved()
        {
            var member = new TrackedDisposable();
            _container.Register(member).ToLogicList<IThing>().AsTransient();

            _container.UnregisterInstance(member);

            Assert.Throws<InvalidOperationException>(() => _container.Resolve<IReadOnlyList<IThing>>(),
                "The IReadOnlyList<T> alias must be torn down along with List<T> once the list " +
                "becomes empty, not left dangling.");
        }

        [Test]
        public void ToViewList_MembersTrackedIndependentlyOfLogicDomain()
        {
            var member = new TrackedDisposable();
            _container.Register(member).ToViewList<IThing>().AsTransient();

            using (_container.BeginViewScope())
            {
                var list = _container.Resolve<List<IThing>>();
                CollectionAssert.Contains(list, member);
            }

            _container.UnregisterInstance(member);

            Assert.That(member.DisposeCount, Is.EqualTo(1),
                "UnregisterInstance must work symmetrically for view-domain list bindings.");
        }
    }
}
