using System;
using mvcExpress.Internal.DependencyInjection;
using NUnit.Framework;

namespace mvcExpress.Tests
{
    // Regression coverage: _instanceKeyRefCounts (MvcDiContainer.cs) was a
    // plain Dictionary<object,int>, keyed with default object equality instead of reference identity.
    // Two DISTINCT instances whose type overrides Equals/GetHashCode (a value-object service, or a
    // boxed struct, which gets structural equality for free with no override needed) collide onto
    // the same ref-count entry. Because the merge only ever inflates the shared count, it can only
    // ever DELAY a zero-crossing, never bring one forward - so the observable failure is a silent,
    // permanent leak of one of the two instances (Dispose() and OnCleanup() never run for it), not a
    // double-dispose or a premature one.
    [TestFixture]
    public class MvcDiContainer_ReferenceCountIdentity_UnitTests
    {
        private interface IThing { }
        private interface IOtherThing { }

        // Overrides value equality so two distinct instances with the same Key compare equal - the
        // exact shape the audit's own recommendation is about. Non-idempotent Dispose() proves
        // "disposed exactly once" rather than merely "disposed at least once" if the fix ever
        // regresses in the other direction (double-dispose).
        private sealed class ValueEqualDisposable : IThing, IOtherThing, IDisposable
        {
            public readonly int Key;
            public int DisposeCount { get; private set; }

            public ValueEqualDisposable(int key) => Key = key;

            public void Dispose()
            {
                DisposeCount++;
                if (DisposeCount > 1)
                    throw new InvalidOperationException("Dispose() called more than once.");
            }

            public override bool Equals(object obj) => obj is ValueEqualDisposable other && other.Key == Key;
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
        public void TwoValueEqualInstances_DirectKeyAndListMember_BothEventuallyDisposedExactlyOnce()
        {
            var first = new ValueEqualDisposable(1);
            var second = new ValueEqualDisposable(1); // distinct instance, but Equals()/GetHashCode() collide
            Assert.That(first, Is.EqualTo(second), "Precondition: the two instances must be value-equal.");
            Assert.That(ReferenceEquals(first, second), Is.False, "Precondition: distinct instances.");

            // Independent registrations under different keys, so the global-type-identity guard
            // does not block this setup - only their VALUE equality collides, not their container keys.
            _container.Register(first).ToLogicList<IThing>().AsTransient();
            _container.Register(second).ToLogicAs<IOtherThing>().AsTransient();

            _container.UnregisterInstance(first);
            Assert.That(first.DisposeCount, Is.EqualTo(1),
                "Removing the list member that was `first`'s only reference must dispose `first` " +
                "exactly once - it must not be treated as still-referenced just because a distinct, " +
                "value-equal instance is registered elsewhere in the container.");

            _container.Unregister<IOtherThing>();
            Assert.That(second.DisposeCount, Is.EqualTo(1),
                "Removing the direct key that was `second`'s only reference must dispose `second` " +
                "exactly once.");
        }

        [Test]
        public void TwoValueEqualInstances_UnregisteredInReverseOrder_BothEventuallyDisposedExactlyOnce()
        {
            var first = new ValueEqualDisposable(2);
            var second = new ValueEqualDisposable(2);

            _container.Register(first).ToLogicAs<IThing>().AsTransient();
            _container.Register(second).ToLogicList<IOtherThing>().AsTransient();

            // Reverse order from the test above - the fix must not depend on which colliding
            // instance's last reference happens to be released first.
            _container.UnregisterInstance(second);
            Assert.That(second.DisposeCount, Is.EqualTo(1));

            _container.Unregister<IThing>();
            Assert.That(first.DisposeCount, Is.EqualTo(1));
        }
    }
}
