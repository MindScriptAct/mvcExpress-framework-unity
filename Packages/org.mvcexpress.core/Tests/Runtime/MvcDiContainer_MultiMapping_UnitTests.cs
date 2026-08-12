using NUnit.Framework;
using mvcExpress.Internal.DependencyInjection;
using System;

namespace mvcExpress.Tests
{
    public class MvcDiContainer_MultiMapping_UnitTests
    {
        private MvcDiContainer _container;

        private interface IReadOnlyThing { }
        private interface IMeasurementThing { }
        private class MultiMapProxy : IReadOnlyThing, IMeasurementThing { }

        [SetUp]
        public void SetUp()
        {
            _container = new MvcDiContainer();
        }

        [TearDown]
        public void TearDown()
        {
            _container.Dispose();
        }

        [Test]
        public void ToLogicAs_CalledTwiceWithDifferentTypes_BothResolve()
        {
            var proxy = new MultiMapProxy();
            _container.Register(proxy)
                .ToLogicAs<IReadOnlyThing>()
                .ToLogicAs<IMeasurementThing>()
                .AsPermanent();

            Assert.That(_container.Resolve<IReadOnlyThing>(), Is.SameAs(proxy));
            Assert.That(_container.Resolve<IMeasurementThing>(), Is.SameAs(proxy));
        }

        [Test]
        public void ToLogic_ThenTwoToLogicAs_AllThreeResolve()
        {
            var proxy = new MultiMapProxy();
            _container.Register(proxy)
                .ToLogic()
                .ToLogicAs<IReadOnlyThing>()
                .ToLogicAs<IMeasurementThing>()
                .AsPermanent();

            Assert.That(_container.Resolve<MultiMapProxy>(), Is.SameAs(proxy));
            Assert.That(_container.Resolve<IReadOnlyThing>(), Is.SameAs(proxy));
            Assert.That(_container.Resolve<IMeasurementThing>(), Is.SameAs(proxy));
        }

        [Test]
        public void ToViewAs_CalledTwiceWithDifferentTypes_BothResolveInViewScope()
        {
            var proxy = new MultiMapProxy();
            _container.Register(proxy)
                .ToViewAs<IReadOnlyThing>()
                .ToViewAs<IMeasurementThing>()
                .AsPermanent();

            using (_container.BeginViewScope())
            {
                Assert.That(_container.Resolve<IReadOnlyThing>(), Is.SameAs(proxy));
                Assert.That(_container.Resolve<IMeasurementThing>(), Is.SameAs(proxy));
            }
        }

        [Test]
        public void SameInterface_MappedToBothLogicAndView_IsNotADuplicate()
        {
            var proxy = new MultiMapProxy();
            Assert.DoesNotThrow(() =>
                _container.Register(proxy)
                    .ToLogicAs<IReadOnlyThing>()
                    .ToViewAs<IReadOnlyThing>()
                    .AsPermanent());

            Assert.That(_container.Resolve<IReadOnlyThing>(), Is.SameAs(proxy));
            using (_container.BeginViewScope())
            {
                Assert.That(_container.Resolve<IReadOnlyThing>(), Is.SameAs(proxy));
            }
        }

        [Test]
        public void ToLogicAs_SameTypeTwiceInOneChain_ThrowsImmediately()
        {
            var proxy = new MultiMapProxy();
            var builder = _container.Register(proxy).ToLogicAs<IReadOnlyThing>();

            var ex = Assert.Throws<InvalidOperationException>(
                () => builder.ToLogicAs<IReadOnlyThing>());
            Assert.That(ex.Message, Does.Contain("IReadOnlyThing"));
            Assert.That(ex.Message, Does.Contain("chain"));
        }

        [Test]
        public void ToLogic_TwiceInOneChain_ThrowsImmediately()
        {
            var proxy = new MultiMapProxy();
            var builder = _container.Register(proxy).ToLogic();

            Assert.Throws<InvalidOperationException>(() => builder.ToLogic());
        }

        [Test]
        public void ToLogic_ThenToLogicAsConcreteType_ThrowsImmediately()
        {
            var proxy = new MultiMapProxy();
            var builder = _container.Register(proxy).ToLogic();

            Assert.Throws<InvalidOperationException>(() => builder.ToLogicAs<MultiMapProxy>());
        }

        [Test]
        public void ToViewAs_SameTypeTwiceInOneChain_ThrowsImmediately()
        {
            var proxy = new MultiMapProxy();
            var builder = _container.Register(proxy).ToViewAs<IReadOnlyThing>();

            var ex = Assert.Throws<InvalidOperationException>(
                () => builder.ToViewAs<IReadOnlyThing>());
            Assert.That(ex.Message, Does.Contain("IReadOnlyThing"));
        }

        [Test]
        public void DuplicateInChain_LeavesContainerUntouched()
        {
            var proxy = new MultiMapProxy();
            var builder = _container.Register(proxy).ToLogicAs<IReadOnlyThing>();
            Assert.Throws<InvalidOperationException>(() => builder.ToLogicAs<IReadOnlyThing>());

            // The chain never completed (AsPermanent was never reached), so nothing should resolve.
            Assert.Throws<InvalidOperationException>(() => _container.Resolve<IReadOnlyThing>());
        }

        private interface IThirdThing { }

        [Test]
        public void ChainWithThreeLogicTypes_SecondCollidesWithExisting_NoneOfThreeAreAdded()
        {
            // Pre-existing registration occupies IMeasurementThing in logic scope.
            var existing = new MultiMapProxy();
            _container.Register(existing).ToLogicAs<IMeasurementThing>().AsPermanent();

            var second = new MultiMapProxyWithThirdThing();
            Assert.Throws<InvalidOperationException>(() =>
                _container.Register(second)
                    .ToLogicAs<IReadOnlyThing>()
                    .ToLogicAs<IMeasurementThing>() // collides with 'existing'
                    .ToLogicAs<IThirdThing>()
                    .AsPermanent());

            // None of the three types from the failed chain should be registered - not even
            // IReadOnlyThing/IThirdThing, which never collided with anything.
            Assert.Throws<InvalidOperationException>(() => _container.Resolve<IReadOnlyThing>());
            Assert.Throws<InvalidOperationException>(() => _container.Resolve<IThirdThing>());

            // The pre-existing registration must be untouched.
            Assert.That(_container.Resolve<IMeasurementThing>(), Is.SameAs(existing));
        }

        private class MultiMapProxyWithThirdThing : IReadOnlyThing, IMeasurementThing, IThirdThing { }

        [Test]
        public void GlobalStyleUsage_SameContainerClass_AccumulatesMappings()
        {
            // Global.Register(...) is MvcDiContainer.Register(...) on a different instance of the
            // same class (see MvcFacade._globalContainer) - this test documents that the fix in
            // this file automatically covers Global without a separate code path.
            var globalContainer = new MvcDiContainer();
            var proxy = new MultiMapProxy();

            globalContainer.Register(proxy)
                .ToLogicAs<IReadOnlyThing>()
                .ToLogicAs<IMeasurementThing>()
                .AsPermanent();

            Assert.That(globalContainer.Resolve<IReadOnlyThing>(), Is.SameAs(proxy));
            Assert.That(globalContainer.Resolve<IMeasurementThing>(), Is.SameAs(proxy));

            globalContainer.Dispose();
        }
    }
}
