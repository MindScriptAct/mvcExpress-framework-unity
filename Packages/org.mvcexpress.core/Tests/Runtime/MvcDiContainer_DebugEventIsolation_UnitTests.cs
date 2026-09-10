using System;
using System.Text.RegularExpressions;
using mvcExpress.Internal.DependencyInjection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace mvcExpress.Tests
{
    // Regression coverage: RegisterInternal's
    // Editor-only debug-registration events (ProxyRegisteredForDebug/ServiceRegisteredForDebug) used
    // to fire completely unguarded as the LAST statement in RegisterInternal - AFTER phase 2 (direct
    // keys) and phase 3 (list bindings) had already committed. A throwing subscriber (e.g. a broken
    // ProxyHierarchyVisualizer) would propagate out of RegisterInternal uncaught, and
    // RegistrationBuilder.Complete() does not wrap the RegisterInternal(...) call itself in a
    // try/catch (only the later OnNonScopedCommitted callback is) - so the caller would see the
    // registration "fail" via an exception while the container had already fully committed it, a
    // genuine atomicity violation distinct from the dead MonoBehaviour/scoped check already fixed
    // elsewhere.
    [TestFixture]
    public class MvcDiContainer_DebugEventIsolation_UnitTests
    {
        private sealed class PlainService { }
        private interface IMarker { }
        private sealed class MarkerService : IMarker { }

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
        public void ThrowingServiceRegisteredForDebugSubscriber_RegistrationStillCommitsWithoutThrowing()
        {
            Action<object> throwingHandler = _ => throw new InvalidOperationException("Boom.");
            _container.ServiceRegisteredForDebug += throwingHandler;

            try
            {
                LogAssert.Expect(LogType.Error, new Regex("debug-registration subscriber threw"));
                LogAssert.Expect(LogType.Exception, new Regex("Boom"));

                var service = new PlainService();
                Assert.DoesNotThrow(() => _container.Register(service).ToLogic().AsPermanent(),
                    "A throwing debug-registration subscriber must not make the registration call " +
                    "itself throw - the subscriber's failure must be isolated from the operation's " +
                    "own success/failure.");

                Assert.That(_container.Resolve<PlainService>(), Is.SameAs(service),
                    "The registration must be fully committed and resolvable despite the " +
                    "subscriber's exception - it must not be rolled back either, since the " +
                    "operation itself succeeded.");
            }
            finally
            {
                _container.ServiceRegisteredForDebug -= throwingHandler;
            }
        }

        [Test]
        public void ThrowingProxyRegisteredForDebugSubscriber_RegistrationStillCommitsWithoutThrowing()
        {
            Action<object> throwingHandler = _ => throw new InvalidOperationException("Boom.");
            _container.ProxyRegisteredForDebug += throwingHandler;

            try
            {
                LogAssert.Expect(LogType.Error, new Regex("debug-registration subscriber threw"));
                LogAssert.Expect(LogType.Exception, new Regex("Boom"));

                var proxy = new MinimalProxy();
                Assert.DoesNotThrow(() => _container.Register(proxy).ToLogicAs<IMinimalProxy>().AsPermanent());

                Assert.That(_container.Resolve<IMinimalProxy>(), Is.SameAs(proxy),
                    "The Proxy registration must be fully committed despite the subscriber's exception.");
            }
            finally
            {
                _container.ProxyRegisteredForDebug -= throwingHandler;
            }
        }

        private interface IMinimalProxy { }
        private sealed class MinimalProxy : mvcExpress.Proxy, IMinimalProxy { }
    }
}
