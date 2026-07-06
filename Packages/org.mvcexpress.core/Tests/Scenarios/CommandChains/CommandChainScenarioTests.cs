using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using mvcExpress.Internal.Commands;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests.Scenarios
{
    /// <summary>
    /// End-to-end command dispatch scenarios.
    /// These tests intentionally build the same runtime pieces a module owns at play time:
    /// a DI container, a message bus, a command processor, and a lightweight module context.
    /// That keeps the tests fast while still proving the public command and messaging behavior
    /// that users rely on when wiring real mvcExpress applications.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public class CommandChainScenarioTests
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

        private readonly struct ChainMessageA : IMessage { }
        private readonly struct ChainMessageB : IMessage { }
        private readonly struct ChainMessageC : IMessage { }
        private readonly struct PoolMessage : IMessage { }

        private static class ScenarioTrace
        {
            public static readonly List<string> Events = new List<string>();

            public static void Reset()
            {
                Events.Clear();
                AwaitingAsyncCommand.Reset();
                ThrowingAsyncCommand.Reset();
                PooledCommand.Reset();
            }
        }

        private sealed class CommandA : Command
        {
            public override void Execute()
            {
                ScenarioTrace.Events.Add("A");
            }
        }

        private sealed class CommandAPublishesMessageB : Command
        {
            public override void Execute()
            {
                ScenarioTrace.Events.Add("A");
                Messenger.Publish<ChainMessageB>();
            }
        }

        private sealed class CommandB : Command
        {
            public override void Execute()
            {
                ScenarioTrace.Events.Add("B");
            }
        }

        private sealed class CommandBPublishesMessageC : Command
        {
            public override void Execute()
            {
                ScenarioTrace.Events.Add("B");
                Messenger.Publish<ChainMessageC>();
            }
        }

        private sealed class CommandC : Command
        {
            public override void Execute()
            {
                ScenarioTrace.Events.Add("C");
            }
        }

        private sealed class AwaitingAsyncCommand : CommandAsync
        {
            public static bool Started;
            public static bool Completed;

            public static void Reset()
            {
                Started = false;
                Completed = false;
            }

            public override async Task ExecuteAsync()
            {
                Started = true;
                await Task.Yield();
                Completed = true;
                ScenarioTrace.Events.Add("async-complete");
            }
        }

        private sealed class ThrowingAsyncCommand : CommandAsync
        {
            public static bool Started;

            public static void Reset()
            {
                Started = false;
            }

            public override async Task ExecuteAsync()
            {
                Started = true;
                await Task.Yield();
                throw new System.InvalidOperationException("Scenario async failure");
            }
        }

        private sealed class PooledCommand : Command
        {
            public static int CreatedCount;
            public static int ExecuteCount;

            public PooledCommand()
            {
                CreatedCount++;
            }

            public static void Reset()
            {
                CreatedCount = 0;
                ExecuteCount = 0;
            }

            protected override void OnInitialize()
            {
                ScenarioTrace.Events.Add("initialize");
            }

            public override void Execute()
            {
                ExecuteCount++;
                ScenarioTrace.Events.Add("execute");
            }
        }

        [SetUp]
        public void SetUp()
        {
            ScenarioTrace.Reset();

            _moduleGo = new GameObject("CommandChainScenarioModule");
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
        public void CommandChain_PublishMessageA_CommandAExecutes()
        {
            _processor.BindCommand<CommandA, ChainMessageA>();

            _messageBus.Publish<ChainMessageA>();

            Assert.That(ScenarioTrace.Events, Is.EqualTo(new[] { "A" }),
                "Publishing the bound message should execute the command once through the same bus path a module uses at runtime.");
        }

        [Test]
        public void CommandChain_CommandAPublishesMessageB_CommandBExecutes()
        {
            _processor.BindCommand<CommandAPublishesMessageB, ChainMessageA>();
            _processor.BindCommand<CommandB, ChainMessageB>();

            _messageBus.Publish<ChainMessageA>();

            Assert.That(ScenarioTrace.Events, Is.EqualTo(new[] { "A", "B" }),
                "A command may publish a follow-up message, and the command bound to that message should run immediately on the same bus.");
        }

        [Test]
        public void CommandChain_FullChain_ExecutionOrderIsSequential()
        {
            _processor.BindCommand<CommandAPublishesMessageB, ChainMessageA>();
            _processor.BindCommand<CommandBPublishesMessageC, ChainMessageB>();
            _processor.BindCommand<CommandC, ChainMessageC>();

            _messageBus.Publish<ChainMessageA>();

            Assert.That(ScenarioTrace.Events, Is.EqualTo(new[] { "A", "B", "C" }),
                "Nested command-triggered publishes should complete depth-first and preserve the user-visible order of the command chain.");
        }

        [Test]
        public async Task AsyncCommand_ExecuteAsync_CompletionAwaited()
        {
            await _processor.RunAsync<AwaitingAsyncCommand>();

            Assert.That(AwaitingAsyncCommand.Started, Is.True,
                "The async command should be initialized and entered before RunAsync returns.");
            Assert.That(AwaitingAsyncCommand.Completed, Is.True,
                "RunAsync should await ExecuteAsync instead of returning after the first incomplete await.");
            Assert.That(ScenarioTrace.Events, Is.EqualTo(new[] { "async-complete" }),
                "Post-await work is the observable proof that command completion was awaited.");
        }

        [Test]
        public async Task AsyncCommand_ExceptionInExecuteAsync_PropagatesCorrectly()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LogAssert.Expect(LogType.Error, new Regex("Async command 'ThrowingAsyncCommand' execution failed"));
#endif

            await _processor.RunAsync<ThrowingAsyncCommand>();

            Assert.That(ThrowingAsyncCommand.Started, Is.True,
                "The command processor owns async command failures: it catches the exception, reports it through logging, and keeps dispatch stable.");
        }

        [Test]
        public void CommandPooling_CommandReturnedToPool_ReuseOnNextExecute()
        {
            _processor.BindCommand<PooledCommand, PoolMessage>(poolSize: 1);

            _messageBus.Publish<PoolMessage>();
            _messageBus.Publish<PoolMessage>();

            Assert.That(PooledCommand.ExecuteCount, Is.EqualTo(2),
                "Both publishes should execute the command even when the command instance is pooled.");
            Assert.That(PooledCommand.CreatedCount, Is.EqualTo(1),
                "A pool size of one should reuse the same valid command instance for consecutive dispatches.");
            Assert.That(ScenarioTrace.Events, Is.EqualTo(new[] { "initialize", "execute", "execute" }),
                "OnInitialize runs once when the command object is first created. Pooled reuse skips it; only Execute runs on subsequent dispatches.");
        }
    }
}

