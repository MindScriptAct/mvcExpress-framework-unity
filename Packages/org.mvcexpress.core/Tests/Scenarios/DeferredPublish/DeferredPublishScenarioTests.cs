using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using mvcExpress.Internal.Messaging;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests.Scenarios
{
    /// <summary>
    /// End-to-end coverage for <see cref="MessengerApi.PublishDeferred{TMessage,T1}"/>: enqueuing
    /// a publish from a background thread and observing it delivered on the Unity main thread
    /// during the next <c>MvcFacade.Update()</c> drain, plus the silent-drop behavior when no
    /// facade instance exists to receive the enqueue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>PublishDeferred</c> lives on <see cref="MessengerApi"/>, the actor-facing API exposed to
    /// Services, Proxies, Commands, and Modules. <c>MvcModule.Messenger</c> is <c>protected</c>, so
    /// the call must happen from inside a module subclass (mirroring how real game code would call
    /// it) rather than reaching in from the test fixture. <see cref="MessengerApi"/> itself has no
    /// <c>Subscribe</c> - only <see cref="MediatorMessengerApi"/> (exposed to
    /// <see cref="MediatorBehaviour"/>) can subscribe - so the scenario needs a companion mediator
    /// to observe delivery.
    /// </para>
    /// <para>
    /// The "no facade" test does not need a reflection workaround: <c>org.mvcexpress.core.Tests.Scenarios</c>
    /// is not covered by the <c>InternalsVisibleTo</c> grant in
    /// <c>Packages/org.mvcexpress.core/Runtime/AssemblyInfo.cs</c> (only
    /// <c>org.mvcexpress.core.Tests</c>, <c>org.mvcexpress.core.Editor</c>, and
    /// <c>org.mvcexpress.console.Editor</c> are listed), so <c>MvcFacade.TryEnqueueDeferredPublish</c>
    /// cannot be called directly from this assembly. Instead, the test keeps a live C# reference to
    /// a module whose GameObject (and the facade's GameObject) have both been destroyed, then calls
    /// the module's own public wrapper for <c>PublishDeferred</c>. That routes through the real
    /// <see cref="MessengerApi.PublishDeferred{TMessage,T1}"/> -&gt; <c>MvcFacade.TryEnqueueDeferredPublish</c>
    /// path and exercises the actual "facade instance is null" branch without any reflection.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("Scenario")]
    public class DeferredPublishScenarioTests
    {
        private readonly struct DeferredMessage : IMessage<int> { }

        private sealed class DeferredMediator : MediatorBehaviour
        {
            public static int ReceivedValue = -1;
            public static int ReceivedThreadId = -1;

            protected override void OnInitialized()
            {
                Messenger.Subscribe<DeferredMessage, int>(value =>
                {
                    ReceivedValue = value;
                    ReceivedThreadId = Thread.CurrentThread.ManagedThreadId;
                });
            }
        }

        private sealed class DeferredModule : MvcModule
        {
            protected override void AttachMediators()
            {
                var go = new GameObject(nameof(DeferredMediator));
                go.transform.SetParent(transform);
                MediatorHub.Attach(go.AddComponent<DeferredMediator>());
            }

            /// <summary>Public wrapper so the test can drive PublishDeferred from outside the module (Messenger is protected).</summary>
            public void PublishDeferredNow(int value)
            {
                Messenger.PublishDeferred<DeferredMessage, int>(value);
            }
        }

        private GameObject _moduleGo;

        [SetUp]
        public void SetUp()
        {
            DeferredMediator.ReceivedValue = -1;
            DeferredMediator.ReceivedThreadId = -1;
        }

        [TearDown]
        public void TearDown()
        {
            if (_moduleGo != null) Object.DestroyImmediate(_moduleGo);

            var facade = MvcFacade.InstanceOrNull;
            if (facade != null) Object.DestroyImmediate(facade.gameObject);
        }

        [UnityTest]
        public IEnumerator PublishDeferred_FromBackgroundThread_DeliversOnMainThreadNextFrame()
        {
            _moduleGo = new GameObject(nameof(DeferredModule));
            var module = _moduleGo.AddComponent<DeferredModule>();

            int mainThreadId = Thread.CurrentThread.ManagedThreadId;

            var backgroundTask = Task.Run(() => module.PublishDeferredNow(7));

            // Block the main thread (no yield, so no Unity frame/Update boundary can occur) until
            // the background thread has finished enqueuing. WaitUntil/yielding here would be racy:
            // Unity may run MvcFacade.Update() as part of resolving the wait, draining the queue
            // before we get a chance to observe the "not yet delivered" state.
            backgroundTask.Wait();

            Assert.That(DeferredMediator.ReceivedValue, Is.EqualTo(-1),
                "The deferred publish must not deliver synchronously from the background thread - " +
                "it must be queued for the next main-thread Update().");

            yield return null;

            Assert.That(DeferredMediator.ReceivedValue, Is.EqualTo(7),
                "PublishDeferred enqueued from a worker thread must be delivered to the subscriber " +
                "on the main thread within the next frame's MvcFacade.Update() drain.");
            Assert.That(DeferredMediator.ReceivedThreadId, Is.EqualTo(mainThreadId),
                "The handler must run on the main thread, not the worker thread that called PublishDeferred.");
        }

        [UnityTest]
        public IEnumerator PublishDeferred_AfterFacadeDestroyed_DroppedSilentlyWithoutError()
        {
            _moduleGo = new GameObject(nameof(DeferredModule));
            var module = _moduleGo.AddComponent<DeferredModule>();

            var facade = MvcFacade.InstanceOrNull;
            Assert.That(facade, Is.Not.Null, "Precondition: facade must exist once a module has registered.");

            // Destroy the module and the facade GameObject, but keep the C# 'module' reference
            // alive so we can still call its public API afterward - this is the only way to reach
            // PublishDeferred with no live facade, since MessengerApi has no static/standalone
            // entry point independent of an actor instance.
            Object.DestroyImmediate(_moduleGo);
            _moduleGo = null;
            Object.DestroyImmediate(facade.gameObject);

            Assert.That(MvcFacade.InstanceOrNull, Is.Null,
                "Precondition: no facade instance should exist after its GameObject is destroyed.");

            // TryEnqueueDeferredPublish must see _facadeInstance == null and return false,
            // dropping the action silently rather than throwing a NullReferenceException.
            Assert.DoesNotThrow(() => module.PublishDeferredNow(7),
                "PublishDeferred called after the facade has been destroyed must be dropped silently " +
                "(TryEnqueueDeferredPublish returns false) rather than throwing a NullReferenceException.");

            yield return null;

            Assert.That(DeferredMediator.ReceivedValue, Is.EqualTo(-1),
                "No subscriber should ever observe a deferred publish enqueued with no facade present " +
                "to drain it.");
        }
    }
}
