using System;
using System.Threading.Tasks;
using mvcExpress.Internal.Commands;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests.Scenarios
{
    /// <summary>
    /// End-to-end messaging scenarios for the framework boundary.
    /// Services are deliberately kept out of the message bus. When service work must produce
    /// an application event, a Command or CommandAsync calls the service and publishes the
    /// resulting message. Proxies and mediators use the same bus to complete the MVC loop.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public class MessagingScenarioTests
    {
        private MvcDiContainer _container;
        private MvcMessageBus _messageBus;
        private MvcCommandProcessor _processor;
        private GameObject _moduleGo;
        private NoInitModule _module;

        private sealed class NoInitModule : MvcModule
        {
            protected override void Awake() { }
            protected override void OnDestroy() { }
        }

        private readonly struct ServiceResultMessage : IMessage<string> { }
        private readonly struct ProxyStateChangedMessage : IMessage<int> { }
        private readonly struct HighFrequencyStructMessage : IMessage<int> { }
        private readonly struct MediatorLifecycleMessage : IMessage<int> { }
        private readonly struct OnePayloadMessage : IMessage<int> { }
        private readonly struct TwoPayloadMessage : IMessage<int, string> { }

        private sealed class AsyncGreetingService
        {
            public async Task<string> BuildGreetingAsync(string name)
            {
                await Task.Yield();
                return "Hello, " + name;
            }
        }

        private sealed class GreetingCommand : CommandAsync<string>
        {
            [Inject] private AsyncGreetingService _service;

            public override async Task ExecuteAsync(string name)
            {
                var greeting = await _service.BuildGreetingAsync(name);
                Messenger.Publish<ServiceResultMessage, string>(greeting);
            }
        }

        private sealed class ScenarioProxy : Proxy
        {
            public void PublishState(int value)
            {
                Messenger.Publish<ProxyStateChangedMessage, int>(value);
            }
        }

        private sealed class ProxyTriggeredCommand : Command<int>
        {
            public static int ExecuteCount;
            public static int LastValue;

            public static void Reset()
            {
                ExecuteCount = 0;
                LastValue = 0;
            }

            public override void Execute(int value)
            {
                ExecuteCount++;
                LastValue = value;
            }
        }

        private sealed class ServiceResultMediator : MediatorBehaviour
        {
            public static int ReceivedCount;
            public static string LastGreeting;

            public static void Reset()
            {
                ReceivedCount = 0;
                LastGreeting = null;
            }

            protected override void OnInitialized()
            {
                Messenger.Subscribe<ServiceResultMessage, string>(OnServiceResult);
            }

            private void OnServiceResult(string greeting)
            {
                ReceivedCount++;
                LastGreeting = greeting;
            }
        }

        private sealed class LifecycleMediator : MediatorBehaviour
        {
            public static int ReceivedCount;
            public static int LastValue;

            public static void Reset()
            {
                ReceivedCount = 0;
                LastValue = 0;
            }

            protected override void OnInitialized()
            {
                Messenger.Subscribe<MediatorLifecycleMessage, int>(OnLifecycleMessage);
            }

            private void OnLifecycleMessage(int value)
            {
                ReceivedCount++;
                LastValue = value;
            }
        }

        private sealed class OnePayloadCommand : Command<int>
        {
            public static int ExecuteCount;
            public static int LastValue;

            public static void Reset()
            {
                ExecuteCount = 0;
                LastValue = 0;
            }

            public override void Execute(int value)
            {
                ExecuteCount++;
                LastValue = value;
            }
        }

        private sealed class TwoPayloadCommand : Command<int, string>
        {
            public static int ExecuteCount;
            public static int LastNumber;
            public static string LastText;

            public static void Reset()
            {
                ExecuteCount = 0;
                LastNumber = 0;
                LastText = null;
            }

            public override void Execute(int number, string text)
            {
                ExecuteCount++;
                LastNumber = number;
                LastText = text;
            }
        }

        private static int _structMessageReceivedCount;
        private static int _structMessageTotal;

        private static void ResetStructCounters()
        {
            _structMessageReceivedCount = 0;
            _structMessageTotal = 0;
        }

        private static void OnHighFrequencyStructMessage(int value)
        {
            _structMessageReceivedCount++;
            _structMessageTotal += value;
        }

        [SetUp]
        public void SetUp()
        {
            ServiceResultMediator.Reset();
            LifecycleMediator.Reset();
            ProxyTriggeredCommand.Reset();
            OnePayloadCommand.Reset();
            TwoPayloadCommand.Reset();
            ResetStructCounters();

            _moduleGo = new GameObject("MessagingScenarioModule");
            _module = _moduleGo.AddComponent<NoInitModule>();
            _container = new MvcDiContainer();
            _messageBus = new MvcMessageBus();
            _processor = new MvcCommandProcessor(typeof(NoInitModule), _container, _messageBus, _module);
        }

        [TearDown]
        public void TearDown()
        {
            _processor?.Dispose();
            _messageBus?.Dispose();
            _container?.Dispose();

            if (_moduleGo != null)
            {
                Object.DestroyImmediate(_moduleGo);
                _moduleGo = null;
                _module = null;
            }
        }

        [Test]
        public async Task SimplePublishSubscribe_CommandAwaitsServiceAndPublishesMessage_MediatorReceives()
        {
            _container.Register(new AsyncGreetingService()).ToLogic().AsPermanent();
            var mediator = CreateMediator<ServiceResultMediator>("ServiceResultMediator");

            await _processor.RunAsync<GreetingCommand, string>("mvcExpress");

            Assert.That(ServiceResultMediator.ReceivedCount, Is.EqualTo(1),
                "The service should not publish directly. The async command awaits it, then publishes the message observed by the mediator.");
            Assert.That(ServiceResultMediator.LastGreeting, Is.EqualTo("Hello, mvcExpress"),
                "The payload should be the exact service result forwarded by the command-owned publish step.");

            Object.DestroyImmediate(mediator.gameObject);
        }

        [Test]
        public void SimplePublishSubscribe_ProxyPublishesMessage_CommandExecutes()
        {
            var proxy = new ScenarioProxy();
            proxy.Initialize(typeof(NoInitModule), _messageBus, _container);
            _processor.BindCommand<ProxyTriggeredCommand, ProxyStateChangedMessage, int>();

            proxy.PublishState(42);

            Assert.That(ProxyTriggeredCommand.ExecuteCount, Is.EqualTo(1),
                "A proxy publish should be routed through the module bus to the command bound to that message type.");
            Assert.That(ProxyTriggeredCommand.LastValue, Is.EqualTo(42),
                "The command should receive the payload value supplied by the proxy publish.");

            proxy.Dispose();
        }

        [Test]
        public void StructMessage_HighFrequencyPublish_ZeroAllocation()
        {
            _messageBus.Subscribe<HighFrequencyStructMessage, int>(OnHighFrequencyStructMessage);
            _messageBus.Publish<HighFrequencyStructMessage, int>(1);
            ResetStructCounters();

            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                _messageBus.Publish<HighFrequencyStructMessage, int>(1);
            }
            var afterBytes = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(_structMessageReceivedCount, Is.EqualTo(1000),
                "Every high-frequency publish should dispatch through the struct message path.");
            Assert.That(_structMessageTotal, Is.EqualTo(1000),
                "The handler should receive the payload without boxing or wrapper-message allocation.");
            Assert.That(afterBytes - beforeBytes, Is.EqualTo(0),
                "After subscription and warmup, direct bus publish of a struct message should not allocate on the current thread.");
        }

        [Test]
        public void MediatorSubscription_OnInitialized_ReceivesMessages()
        {
            var mediator = CreateMediator<LifecycleMediator>("LifecycleMediator");

            _messageBus.Publish<MediatorLifecycleMessage, int>(7);

            Assert.That(LifecycleMediator.ReceivedCount, Is.EqualTo(1),
                "A mediator should subscribe during OnInitialized and receive messages published afterward.");
            Assert.That(LifecycleMediator.LastValue, Is.EqualTo(7),
                "The mediator subscription should receive the payload from the message bus.");

            Object.DestroyImmediate(mediator.gameObject);
        }

        [Test]
        public void MediatorSubscription_OnDestroy_NoLongerReceivesMessages()
        {
            var mediator = CreateMediator<LifecycleMediator>("LifecycleMediator");
            _messageBus.Publish<MediatorLifecycleMessage, int>(1);

            Object.DestroyImmediate(mediator.gameObject);
            _messageBus.Publish<MediatorLifecycleMessage, int>(2);

            Assert.That(LifecycleMediator.ReceivedCount, Is.EqualTo(1),
                "Destroying the mediator should automatically remove subscriptions tracked during OnInitialized.");
            Assert.That(LifecycleMediator.LastValue, Is.EqualTo(1),
                "The destroyed mediator should not observe messages published after cleanup.");
        }

        [Test]
        public void MultiArityCommand_OnePayload_PayloadForwardedCorrectly()
        {
            _processor.BindCommand<OnePayloadCommand, OnePayloadMessage, int>();

            _messageBus.Publish<OnePayloadMessage, int>(123);

            Assert.That(OnePayloadCommand.ExecuteCount, Is.EqualTo(1),
                "A one-payload message should execute the one-payload command bound to it.");
            Assert.That(OnePayloadCommand.LastValue, Is.EqualTo(123),
                "The single payload value should be forwarded unchanged to Command<T1>.Execute.");
        }

        [Test]
        public void MultiArityCommand_TwoPayloads_BothPayloadsForwardedCorrectly()
        {
            _processor.BindCommand<TwoPayloadCommand, TwoPayloadMessage, int, string>();

            _messageBus.Publish<TwoPayloadMessage, int, string>(7, "seven");

            Assert.That(TwoPayloadCommand.ExecuteCount, Is.EqualTo(1),
                "A two-payload message should execute the two-payload command bound to it.");
            Assert.That(TwoPayloadCommand.LastNumber, Is.EqualTo(7),
                "The first payload should be forwarded unchanged to Command<T1,T2>.Execute.");
            Assert.That(TwoPayloadCommand.LastText, Is.EqualTo("seven"),
                "The second payload should be forwarded unchanged to Command<T1,T2>.Execute.");
        }

        private T CreateMediator<T>(string name) where T : MediatorBehaviour
        {
            var go = new GameObject(name);
            go.transform.SetParent(_moduleGo.transform);
            var mediator = go.AddComponent<T>();
            mediator.Initialize(_module, _container, _messageBus);
            return mediator;
        }
    }
}
