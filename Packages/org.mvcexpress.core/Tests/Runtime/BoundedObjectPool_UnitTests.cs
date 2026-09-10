using System;
using System.Threading.Tasks;
using NUnit.Framework;
using mvcExpress.Internal.Utilities;

namespace mvcExpress.Tests
{
    public class BoundedObjectPool_UnitTests
    {
        private class TestObject : IDisposable
        {
            public bool IsDisposed { get; private set; }
            public bool IsReset { get; private set; }

            public void Dispose()
            {
                IsDisposed = true;
            }

            public void Reset()
            {
                IsReset = true;
            }
        }

        private BoundedObjectPool<TestObject> pool;
        private int createCount;

        [SetUp]
        public void SetUp()
        {
            createCount = 0;
        }

        private TestObject Factory()
        {
            createCount++;
            return new TestObject();
        }

        private void ResetObj(TestObject obj) => obj.Reset();

        [Test]
        public void Init_ValidSize_SetsMaxSize()
        {
            pool = new BoundedObjectPool<TestObject>(Factory, ResetObj, 5, 4);
            Assert.AreEqual(5, pool.MaxSize);
        }

        [Test]
        public void Get_EmptyPool_CallsFactory()
        {
            pool = new BoundedObjectPool<TestObject>(Factory, ResetObj, 5, 4);
            var obj = pool.Get();
            Assert.IsNotNull(obj);
            Assert.AreEqual(1, createCount);
        }

        [Test]
        public void Get_NonEmptyPool_ReturnsExisting()
        {
            pool = new BoundedObjectPool<TestObject>(Factory, ResetObj, 5, 4);
            var obj1 = pool.Get();
            pool.Return(obj1);
            
            var obj2 = pool.Get();
            Assert.AreSame(obj1, obj2);
            Assert.AreEqual(1, createCount);
        }

        [Test]
        public void Return_SpaceAvailable_ResetsAndPushes()
        {
            pool = new BoundedObjectPool<TestObject>(Factory, ResetObj, 5, 4);
            var obj = pool.Get();
            pool.Return(obj);

            Assert.IsTrue(obj.IsReset);
#if UNITY_EDITOR || MVC_LOGGING
            Assert.AreEqual(1, pool.GetStatistics().TotalReturned);
#endif
        }

        [Test]
        public void Return_FullPool_DisposesAndDiscards()
        {
            pool = new BoundedObjectPool<TestObject>(Factory, ResetObj, 1, 1);
            var obj1 = pool.Get();
            var obj2 = new TestObject();

            pool.Return(obj1);
            pool.Return(obj2);

            Assert.IsTrue(obj2.IsDisposed);
#if UNITY_EDITOR || MVC_LOGGING
            Assert.AreEqual(1, pool.GetStatistics().TotalDiscarded);
#endif
        }

        [Test]
        public void Return_ZeroCapacity_DisposesImmediately()
        {
            pool = new BoundedObjectPool<TestObject>(Factory, ResetObj, 0, 1);
            var obj = pool.Get();
            pool.Return(obj);

            Assert.IsTrue(obj.IsDisposed);
#if UNITY_EDITOR || MVC_LOGGING
            Assert.AreEqual(1, pool.GetStatistics().TotalDiscarded);
#endif
        }

        [Test]
        public void Resize_Grow_UpdatesMaxSize()
        {
            pool = new BoundedObjectPool<TestObject>(Factory, ResetObj, 2, 2);
            pool.Resize(5);
            Assert.AreEqual(5u, pool.MaxSize);
        }

        [Test]
        public void Resize_Shrink_DisposesExcess()
        {
            pool = new BoundedObjectPool<TestObject>(Factory, ResetObj, 5, 5);
            var obj1 = pool.Get();
            var obj2 = pool.Get();
            pool.Return(obj1);
            pool.Return(obj2);

            pool.Resize(1);

            Assert.AreEqual(1u, pool.MaxSize);
            Assert.IsTrue(obj1.IsDisposed || obj2.IsDisposed);
        }

        [Test]
        public void TrimToTargetSize_TrimsCorrectly()
        {
            pool = new BoundedObjectPool<TestObject>(Factory, ResetObj, 5, 5);
            var obj1 = pool.Get();
            var obj2 = pool.Get();
            pool.Return(obj1);
            pool.Return(obj2);

            pool.TrimTo(1);
            Assert.IsTrue(obj1.IsDisposed || obj2.IsDisposed);
        }

        [Test]
        public void Clear_DisposesAll()
        {
            pool = new BoundedObjectPool<TestObject>(Factory, ResetObj, 5, 5);
            var obj1 = pool.Get();
            pool.Return(obj1);

            pool.Clear();

            Assert.IsTrue(obj1.IsDisposed);
        }

        [Test]
        public void Return_NullObject_IsIgnored()
        {
            pool = new BoundedObjectPool<TestObject>(Factory, ResetObj, 5, 4);
            Assert.DoesNotThrow(() => pool.Return(null),
                "Return(null) must be silently ignored, not throw.");
            Assert.AreEqual(0, pool.GetStatistics().TotalReturned,
                "Null return must not increment TotalReturned.");
        }

        [Test]
        public void Get_LIFO_LastReturnedIsFirstRetrieved()
        {
            pool = new BoundedObjectPool<TestObject>(Factory, ResetObj, 5, 4);
            var a = pool.Get();
            var b = pool.Get();
            var c = pool.Get();

            pool.Return(a);
            pool.Return(b);
            pool.Return(c);

            Assert.AreSame(c, pool.Get(), "Third returned (c) must be first retrieved (LIFO).");
            Assert.AreSame(b, pool.Get(), "Second returned (b) must be second retrieved (LIFO).");
            Assert.AreSame(a, pool.Get(), "First returned (a) must be last retrieved (LIFO).");
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// The pool used to lock every operation "just in case" a background thread called it. It doesn't
        /// happen anywhere in this framework's supported design (see the class remarks), so the
        /// lock was removed in favor of an Editor/Development-only assertion that catches a
        /// same-class-of-mistake loudly instead of silently corrupting the unlocked Stack&lt;T&gt;.
        /// </summary>
        [Test]
        public void Get_CalledFromBackgroundThread_ThrowsInvalidOperationException()
        {
            pool = new BoundedObjectPool<TestObject>(Factory, ResetObj, 5, 4);

            var backgroundTask = Task.Run(() => pool.Get());

            var aggregate = Assert.Throws<AggregateException>(() => backgroundTask.Wait());
            Assert.IsInstanceOf<InvalidOperationException>(aggregate.InnerException,
                "Get() called from any thread other than the one that constructed the pool must " +
                "throw InvalidOperationException, not silently run (and not silently corrupt state).");
        }

        [Test]
        public void Return_CalledFromBackgroundThread_ThrowsInvalidOperationException()
        {
            pool = new BoundedObjectPool<TestObject>(Factory, ResetObj, 5, 4);
            var obj = pool.Get();

            var backgroundTask = Task.Run(() => pool.Return(obj));

            var aggregate = Assert.Throws<AggregateException>(() => backgroundTask.Wait());
            Assert.IsInstanceOf<InvalidOperationException>(aggregate.InnerException,
                "Return() called from any thread other than the one that constructed the pool must " +
                "throw InvalidOperationException, not silently run.");
        }
#endif

#if UNITY_EDITOR || MVC_LOGGING
        [Test]
        public void GetStatistics_AccuratelyTracksAllOperations()
        {
            pool = new BoundedObjectPool<TestObject>(Factory, ResetObj, 2, 2);

            var a = pool.Get();
            var b = pool.Get();
            var c = pool.Get(); // factory called 3 times

            var statsAfterGets = pool.GetStatistics();
            Assert.AreEqual(3, statsAfterGets.TotalCreated, "TotalCreated must equal factory invocations.");
            Assert.AreEqual(0, statsAfterGets.TotalReturned, "TotalReturned must be 0 before any returns.");

            pool.Return(a); // pooled
            pool.Return(b); // pooled (now at capacity 2)
            pool.Return(c); // discarded (pool full), TotalDiscarded=1

            var statsAfterReturns = pool.GetStatistics();
            Assert.AreEqual(3, statsAfterReturns.TotalReturned, "TotalReturned must equal return calls.");
            Assert.AreEqual(1, statsAfterReturns.TotalDiscarded, "TotalDiscarded must count discarded items.");
            Assert.AreEqual(2, statsAfterReturns.CurrentSize, "CurrentSize must equal pooled count.");
            Assert.IsTrue(c.IsDisposed, "Discarded item must be disposed.");

            // Pool hit: Get from pool (no factory call), then Return it again.
            // TotalGets goes from 3 to 4, PooledGets goes from 0 to 1.
            var hit = pool.Get(); // pool hit - TotalCreated stays 3, PooledGets becomes 1
            pool.Return(hit);

            var statsAfterHit = pool.GetStatistics();
            Assert.AreEqual(4, statsAfterHit.TotalGets, "TotalGets must count every Get() call.");
            Assert.AreEqual(1, statsAfterHit.PooledGets, "PooledGets must count only Get() calls served from the pool.");
            Assert.AreEqual(0.25f, statsAfterHit.HitRate, 0.0001f,
                "HitRate must equal PooledGets / TotalGets.");
        }

        /// <summary>
        /// Regression coverage: the previous HitRate formula, (TotalReturned - TotalCreated) /
        /// TotalReturned, used TotalReturned as a stand-in for "total gets" - an assumption that
        /// broke as soon as more than one Get() was outstanding (not yet Return()ed) at the same
        /// time, which async command overlap makes a normal occurrence, not an edge case. HitRate
        /// is now computed entirely from counters both incremented inside Get() itself
        /// (TotalGets, PooledGets), so it can never go negative or depend on how many Get()s are
        /// currently checked out.
        /// </summary>
        [Test]
        public void GetStatistics_OverlappingOutstandingGets_HitRateNeverNegative()
        {
            pool = new BoundedObjectPool<TestObject>(Factory, ResetObj, 5, 4);

            // Reproduces the scenario that made the old formula go negative: two Get()s land
            // while only one prior Get() has ever been Return()ed, so "creates" outpaces
            // "returns" at this snapshot even though every Get() is legitimate.
            var first = pool.Get();  // miss: TotalCreated=1
            pool.Return(first);      // pool now holds 1 item
            var second = pool.Get(); // hit: pops the pooled item
            var third = pool.Get();  // miss (pool empty again): TotalCreated=2
            // Neither second nor third has been Return()ed yet - old formula: TotalReturned=1,
            // TotalCreated=2 -> HitRate = (1 - 2) / 1 = -1.

            var stats = pool.GetStatistics();
            Assert.AreEqual(3, stats.TotalGets);
            Assert.AreEqual(1, stats.PooledGets);
            Assert.That(stats.HitRate, Is.GreaterThanOrEqualTo(0f),
                "HitRate must never be negative, even with multiple Get()s outstanding.");
            Assert.AreEqual(1f / 3f, stats.HitRate, 0.0001f);

            pool.Return(second);
            pool.Return(third);
        }
#endif
    }
}
