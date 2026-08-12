using System;
using mvcExpress.Internal.DependencyInjection;
using NUnit.Framework;

namespace mvcExpress.Tests
{
    [TestFixture]
    public class MvcDiContainer_DualKeyUnregister_UnitTests
    {
        private interface IFoo
        {
        }

        private sealed class Foo : IFoo, IDisposable
        {
            public bool Disposed { get; private set; }
            public void Dispose() => Disposed = true;
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
        public void Unregister_OneOfTwoKeys_OtherKeyStillResolvesUndisposedInstance()
        {
            var foo = new Foo();
            _container.Register(foo).ToLogicAs<IFoo>().ToView().AsTransient();

            _container.Unregister<IFoo>();

            Assert.That(foo.Disposed, Is.False,
                "The instance must not be disposed while the view-scope key still references it - " +
                "only the reference count for the IFoo key should have been released.");
            Assert.DoesNotThrow(() =>
                {
                    using (_container.BeginViewScope())
                    {
                        _container.Resolve<Foo>();
                    }
                },
                "The view-scope key (concrete Foo type) must still resolve the live instance after " +
                "only the logic-scope (IFoo) key was unregistered.");
        }

        [Test]
        public void Unregister_BothKeys_InstanceDisposedOnlyAfterLastKeyRemoved()
        {
            var foo = new Foo();
            _container.Register(foo).ToLogicAs<IFoo>().ToView().AsTransient();

            _container.Unregister<IFoo>();
            Assert.That(foo.Disposed, Is.False, "Precondition: first unregister must not dispose yet.");

            _container.Unregister<Foo>();

            Assert.That(foo.Disposed, Is.True,
                "Once the last key referencing the instance (the concrete Foo view-scope key) is " +
                "unregistered, the ref count reaches zero and the instance must be disposed exactly then.");
        }
    }
}
