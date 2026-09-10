using System;
using mvcExpress.Internal.DependencyInjection;
using NUnit.Framework;

namespace mvcExpress.Tests
{
    // Regression coverage: ToLogicAs<T>()/ToViewAs<T>() (and the
    // runtime-Type overloads on the non-generic RegistrationBuilder returned by
    // MvcDiContainer.Register(instance, type)) accepted a mapping the instance does not actually
    // implement. Nothing downstream checks this - the four internal dictionaries are Type -> object,
    // so a mismatched mapping was stored without complaint - and the failure only surfaced later as
    // a cast exception during Resolve<T>() or list consumption, far from the misconfigured
    // .Register(...) call. RegistrationBuilder now validates at the fluent-call site itself.
    [TestFixture]
    public class MvcDiContainer_AssignabilityValidation_UnitTests
    {
        private interface IImplemented { }
        private interface INotImplemented { }
        private interface IGenericMarker<T> { }

        private sealed class Thing : IImplemented { }

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
        public void ToLogicAs_TypeNotImplementedByInstance_Throws()
        {
            var thing = new Thing();
            var ex = Assert.Throws<InvalidOperationException>(
                () => _container.Register(thing).ToLogicAs<INotImplemented>(),
                "ToLogicAs<T>() must validate that the instance actually implements T, not silently " +
                "store a mapping that will only fail later as a cast exception during Resolve<T>().");
            Assert.That(ex.Message, Does.Contain("Thing"));
            Assert.That(ex.Message, Does.Contain("INotImplemented"));
        }

        [Test]
        public void ToViewAs_TypeNotImplementedByInstance_Throws()
        {
            var thing = new Thing();
            var ex = Assert.Throws<InvalidOperationException>(
                () => _container.Register(thing).ToViewAs<INotImplemented>());
            Assert.That(ex.Message, Does.Contain("Thing"));
            Assert.That(ex.Message, Does.Contain("INotImplemented"));
        }

        [Test]
        public void ToLogicList_ElementTypeNotImplementedByInstance_Throws()
        {
            var thing = new Thing();
            Assert.Throws<InvalidOperationException>(
                () => _container.Register(thing).ToLogicList<INotImplemented>(),
                "A list element type the instance doesn't implement must be rejected immediately, " +
                "not deferred to List<T>.Add's own runtime type check inside AddToListBinding.");
        }

        [Test]
        public void ToViewList_ElementTypeNotImplementedByInstance_Throws()
        {
            var thing = new Thing();
            Assert.Throws<InvalidOperationException>(
                () => _container.Register(thing).ToViewList<INotImplemented>());
        }

        [Test]
        public void RuntimeTypeRegistration_ToLogicAs_TypeNotImplementedByInstance_Throws()
        {
            var thing = new Thing();
            var ex = Assert.Throws<InvalidOperationException>(
                () => _container.Register(thing, typeof(Thing)).ToLogicAs(typeof(INotImplemented)),
                "The runtime-Type RegistrationBuilder overload must validate assignability too - it " +
                "has no compile-time generic constraint to lean on at all.");
            Assert.That(ex.Message, Does.Contain("INotImplemented"));
        }

        [Test]
        public void RuntimeTypeRegistration_ToViewAs_TypeNotImplementedByInstance_Throws()
        {
            var thing = new Thing();
            Assert.Throws<InvalidOperationException>(
                () => _container.Register(thing, typeof(Thing)).ToViewAs(typeof(INotImplemented)));
        }

        [Test]
        public void RuntimeTypeRegistration_ToLogicAs_OpenGenericType_ThrowsDistinctMessage()
        {
            var thing = new Thing();
            var ex = Assert.Throws<InvalidOperationException>(
                () => _container.Register(thing, typeof(Thing)).ToLogicAs(typeof(IGenericMarker<>)),
                "An open generic type definition can never be a valid exposed type - no object can " +
                "be an instance of an unbound generic - so it should get its own clear message " +
                "distinct from a plain assignability mismatch.");
            Assert.That(ex.Message, Does.Contain("open generic"));
        }

        [Test]
        public void RuntimeTypeRegistration_ToLogicList_OpenGenericElementType_Throws()
        {
            var thing = new Thing();
            var ex = Assert.Throws<InvalidOperationException>(
                () => _container.Register(thing, typeof(Thing)).ToLogicList(typeof(IGenericMarker<>)));
            Assert.That(ex.Message, Does.Contain("open generic"));
        }

        [Test]
        public void ToLogicAs_LegitimateInterface_StillSucceeds()
        {
            var thing = new Thing();
            Assert.DoesNotThrow(() =>
                _container.Register(thing).ToLogicAs<IImplemented>().AsPermanent());
            Assert.That(_container.Resolve<IImplemented>(), Is.SameAs(thing));
        }
    }
}
