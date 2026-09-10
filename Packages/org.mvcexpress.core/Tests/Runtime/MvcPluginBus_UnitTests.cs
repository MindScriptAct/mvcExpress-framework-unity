using System;
using System.Collections.Generic;
using NUnit.Framework;
using mvcExpress;
using mvcExpress.Logging;
using mvcExpress.Plugins;

namespace mvcExpress.Tests
{
    // Regression coverage for MSG-07: MvcPluginBus dispatched observer lists used to iterate the
    // live backing List<T> by index, so an observer that unregistered itself mid-dispatch shrank
    // the list and caused the next observer to be skipped, and one observer's exception stopped
    // every later observer from being notified for that dispatch.
    public class MvcPluginBus_UnitTests
    {
        private class RecordingLogObserver : ILogObserver
        {
            private readonly List<string> _log;
            private readonly Action _onLog;

            public RecordingLogObserver(List<string> log, Action onLog = null)
            {
                _log = log;
                _onLog = onLog;
            }

            public void OnLog(string message, MvcLogContext context)
            {
                _log.Add(message);
                _onLog?.Invoke();
            }

            public void OnWarning(string message, MvcLogContext context) { }
            public void OnError(string message, MvcLogContext context) { }
        }

        private class ThrowingLogObserver : ILogObserver
        {
            public void OnLog(string message, MvcLogContext context) => throw new Exception("boom");
            public void OnWarning(string message, MvcLogContext context) { }
            public void OnError(string message, MvcLogContext context) { }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private class RecordingModuleObserver : IModuleObserver
        {
            private readonly List<Type> _log;
            private readonly Action _onRegistered;

            public RecordingModuleObserver(List<Type> log, Action onRegistered = null)
            {
                _log = log;
                _onRegistered = onRegistered;
            }

            public void OnModuleRegistered(Type moduleType)
            {
                _log.Add(moduleType);
                _onRegistered?.Invoke();
            }

            public void OnModuleInitialized(Type moduleType) { }
            public void OnModuleUnregistered(Type moduleType) { }
        }

        private class ThrowingModuleObserver : IModuleObserver
        {
            public void OnModuleRegistered(Type moduleType) => throw new Exception("boom");
            public void OnModuleInitialized(Type moduleType) { }
            public void OnModuleUnregistered(Type moduleType) { }
        }

        private class RecordingMessageObserver : IMessageObserver
        {
            private readonly List<string> _log;
            private readonly string _name;
            private readonly Action _onPublished;
            private readonly Action _onDispatched;

            public RecordingMessageObserver(List<string> log, string name, Action onPublished = null, Action onDispatched = null)
            {
                _log = log;
                _name = name;
                _onPublished = onPublished;
                _onDispatched = onDispatched;
            }

            public void OnMessagePublished(Type messageType, Type publisherModuleType)
            {
                _log.Add(_name);
                _onPublished?.Invoke();
            }

            public void OnMessageDispatched(Type messageType, Type publisherModuleType, Type subscriberActorType, Type subscriberModuleType)
            {
                _log.Add(_name);
                _onDispatched?.Invoke();
            }
        }

        private class ThrowingMessageObserver : IMessageObserver
        {
            public void OnMessagePublished(Type messageType, Type publisherModuleType) => throw new Exception("boom");
            public void OnMessageDispatched(Type messageType, Type publisherModuleType, Type subscriberActorType, Type subscriberModuleType) => throw new Exception("boom");
        }
#endif

        private readonly List<ILogObserver> _registeredLogObservers = new List<ILogObserver>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly List<IModuleObserver> _registeredModuleObservers = new List<IModuleObserver>();
        private readonly List<IMessageObserver> _registeredMessageObservers = new List<IMessageObserver>();
#endif

        [TearDown]
        public void TearDown()
        {
            // These lists are static on MvcPluginBus - always unregister test observers so they
            // don't leak into other tests or real dispatch.
            foreach (var observer in _registeredLogObservers) MvcPluginBus.Unregister(observer);
            _registeredLogObservers.Clear();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            foreach (var observer in _registeredModuleObservers) MvcPluginBus.Unregister(observer);
            _registeredModuleObservers.Clear();

            foreach (var observer in _registeredMessageObservers) MvcPluginBus.Unregister(observer);
            _registeredMessageObservers.Clear();
#endif
        }

        private void RegisterLog(ILogObserver observer)
        {
            MvcPluginBus.Register(observer);
            _registeredLogObservers.Add(observer);
        }

        [Test]
        public void FireLog_ObserverUnregistersItselfDuringDispatch_LaterObserverStillNotified()
        {
            var log = new List<string>();
            RecordingLogObserver self = null;
            self = new RecordingLogObserver(log, () => MvcPluginBus.Unregister(self));
            var after = new RecordingLogObserver(log);

            RegisterLog(self);
            RegisterLog(after);

            MvcPluginBus.FireLog("hello", default);

            Assert.AreEqual(2, log.Count, "both observers registered at dispatch start should run for this pass");
            _registeredLogObservers.Remove(self); // already unregistered itself
        }

        [Test]
        public void FireLog_ObserverThrows_IsolationNotAddedAndListStaysUsableAfterward()
        {
            var log = new List<string>();
            RegisterLog(new ThrowingLogObserver());
            RegisterLog(new RecordingLogObserver(log));

            // ILogObserver exceptions are intentionally NOT isolated here (it's a documented
            // production extension point for arbitrary third-party code; silently swallowing its
            // exceptions is the same policy question deferred for the message bus itself) - lock
            // that in explicitly so a future pass doesn't assume the MSG-07 snapshot fix silently
            // added isolation here too.
            Assert.Throws<Exception>(() => MvcPluginBus.FireLog("hello", default));
            Assert.AreEqual(0, log.Count, "the observer after the throwing one should not run for this dispatch");

            // The snapshot fix must not leave the live list corrupted by the aborted dispatch -
            // a later, non-throwing dispatch through the same (unmodified) observer list still
            // works normally.
            MvcPluginBus.Unregister(_registeredLogObservers[0]);
            MvcPluginBus.FireLog("hello again", default);
            Assert.AreEqual(1, log.Count);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void RegisterModule(IModuleObserver observer)
        {
            MvcPluginBus.Register(observer);
            _registeredModuleObservers.Add(observer);
        }

        [Test]
        public void FireModuleRegistered_ObserverUnregistersItselfDuringDispatch_LaterObserverStillNotified()
        {
            var log = new List<Type>();
            RecordingModuleObserver self = null;
            self = new RecordingModuleObserver(log, () => MvcPluginBus.Unregister(self));
            var after = new RecordingModuleObserver(log);

            RegisterModule(self);
            RegisterModule(after);

            MvcPluginBus.FireModuleRegistered(typeof(string));

            Assert.AreEqual(2, log.Count, "both observers registered at dispatch start should run for this pass");
            _registeredModuleObservers.Remove(self); // already unregistered itself
        }

        [Test]
        public void FireModuleRegistered_ObserverThrows_LaterObserverStillNotifiedAndNoExceptionEscapes()
        {
            var log = new List<Type>();
            RegisterModule(new ThrowingModuleObserver());
            RegisterModule(new RecordingModuleObserver(log));

            // Framework-internal editor observers get exception isolation: one broken visualizer
            // must not stop every other module observer from being notified.
            Assert.DoesNotThrow(() => MvcPluginBus.FireModuleRegistered(typeof(string)));
            Assert.AreEqual(1, log.Count);
        }

        // Regression coverage for the MSG-07 follow-up: FireMessagePublished/FireMessageDispatched
        // used to iterate the live list by index and adjust the index whenever the count dropped,
        // which only happened to work for self-unregistration - removing a LATER, not-yet-visited
        // observer double-invoked the current one. These two methods now dispatch over a snapshot
        // taken once at the start of the call, so mutating the live list mid-dispatch can no longer
        // corrupt the in-progress iteration at all.
        private void RegisterMessage(IMessageObserver observer)
        {
            MvcPluginBus.Register(observer);
            _registeredMessageObservers.Add(observer);
        }

        [Test]
        public void FireMessagePublished_ObserverUnregistersItselfDuringDispatch_LaterObserverStillFiresExactlyOnce()
        {
            var log = new List<string>();
            RecordingMessageObserver self = null;
            self = new RecordingMessageObserver(log, "self", onPublished: () => MvcPluginBus.Unregister(self));
            var after = new RecordingMessageObserver(log, "after");

            RegisterMessage(self);
            RegisterMessage(after);

            MvcPluginBus.FireMessagePublished(typeof(string), null);

            Assert.AreEqual(new[] { "self", "after" }, log.ToArray());
            _registeredMessageObservers.Remove(self); // already unregistered itself
        }

        [Test]
        public void FireMessagePublished_ObserverUnregistersALaterObserverDuringDispatch_NeitherIsSkippedNorDoubleInvoked()
        {
            var log = new List<string>();
            RecordingMessageObserver z = null;
            var x = new RecordingMessageObserver(log, "X", onPublished: () => MvcPluginBus.Unregister(z));
            var y = new RecordingMessageObserver(log, "Y");
            z = new RecordingMessageObserver(log, "Z");

            RegisterMessage(x);
            RegisterMessage(y);
            RegisterMessage(z);

            MvcPluginBus.FireMessagePublished(typeof(string), null);

            // The dispatch snapshot is taken once at the start of the call, so Z (removed
            // reentrantly, before its own turn) still completes THIS pass - it just won't be
            // dispatched to on the NEXT call. What this guards against is the actual bug: X being
            // invoked twice, or Y/Z being skipped.
            Assert.AreEqual(new[] { "X", "Y", "Z" }, log.ToArray());
        }

        [Test]
        public void FireMessagePublished_ObserverUnregistersAnEarlierObserverDuringDispatch_RemainingObserversStillFireExactlyOnce()
        {
            var log = new List<string>();
            RecordingMessageObserver x = null;
            x = new RecordingMessageObserver(log, "X");
            var y = new RecordingMessageObserver(log, "Y", onPublished: () => MvcPluginBus.Unregister(x));
            var z = new RecordingMessageObserver(log, "Z");

            RegisterMessage(x);
            RegisterMessage(y);
            RegisterMessage(z);

            MvcPluginBus.FireMessagePublished(typeof(string), null);

            Assert.AreEqual(new[] { "X", "Y", "Z" }, log.ToArray());
            _registeredMessageObservers.Remove(x); // already unregistered by Y
        }

        [Test]
        public void FireMessagePublished_ObserverRegistersANewObserverDuringDispatch_NewObserverDefersToNextDispatch()
        {
            var log = new List<string>();
            var x = new RecordingMessageObserver(log, "X", onPublished: () =>
            {
                RegisterMessage(new RecordingMessageObserver(log, "W"));
            });
            RegisterMessage(x);

            MvcPluginBus.FireMessagePublished(typeof(string), null);
            Assert.AreEqual(new[] { "X" }, log.ToArray(), "an observer registered mid-dispatch must not fire in the same pass");

            MvcPluginBus.FireMessagePublished(typeof(string), null);
            Assert.AreEqual(new[] { "X", "X", "W" }, log.ToArray(), "the next dispatch picks up the newly registered observer normally");
        }

        [Test]
        public void FireMessagePublished_ObserverThrows_LaterObserverStillNotifiedAndNoExceptionEscapes()
        {
            var log = new List<string>();
            RegisterMessage(new ThrowingMessageObserver());
            RegisterMessage(new RecordingMessageObserver(log, "after"));

            Assert.DoesNotThrow(() => MvcPluginBus.FireMessagePublished(typeof(string), null));
            Assert.AreEqual(new[] { "after" }, log.ToArray());
        }

        [Test]
        public void FireMessagePublished_ObserverTriggersNestedDispatchReentrantly_BothPassesCompleteWithoutCorruption()
        {
            var log = new List<string>();
            var nestedTriggered = false;
            var a = new RecordingMessageObserver(log, "A", onPublished: () =>
            {
                if (nestedTriggered) return;
                nestedTriggered = true;
                // Simulates an observer callback that itself triggers another Publish, causing a
                // reentrant call into FireMessagePublished while this outer call is still mid-loop
                // over the shared pooled snapshot buffer.
                MvcPluginBus.FireMessagePublished(typeof(int), null);
            });
            var b = new RecordingMessageObserver(log, "B");

            RegisterMessage(a);
            RegisterMessage(b);

            MvcPluginBus.FireMessagePublished(typeof(string), null);

            // A fires and triggers the nested pass, which dispatches to A and B using its own
            // one-off array; the outer pass then resumes and still correctly reaches B using its
            // own untouched snapshot - proving the reused buffer wasn't corrupted by the nested call.
            Assert.AreEqual(new[] { "A", "A", "B", "B" }, log.ToArray());
        }

        [Test]
        public void FireMessageDispatched_ObserverUnregistersALaterObserverDuringDispatch_NeitherIsSkippedNorDoubleInvoked()
        {
            var log = new List<string>();
            RecordingMessageObserver z = null;
            var x = new RecordingMessageObserver(log, "X", onDispatched: () => MvcPluginBus.Unregister(z));
            var y = new RecordingMessageObserver(log, "Y");
            z = new RecordingMessageObserver(log, "Z");

            RegisterMessage(x);
            RegisterMessage(y);
            RegisterMessage(z);

            MvcPluginBus.FireMessageDispatched(typeof(string), null, typeof(object), null);

            Assert.AreEqual(new[] { "X", "Y", "Z" }, log.ToArray());
        }
#endif
    }
}
