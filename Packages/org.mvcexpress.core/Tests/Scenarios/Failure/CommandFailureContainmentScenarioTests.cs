using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
    /// Pins the documented failure-containment contract (docs audit D11, revised by MSG-02): a
    /// sync command exception is caught by MvcCommandProcessor (dispatch continues, OnCommandFault
    /// fires) in every build, unconditionally. A mediator handler exception is now ALSO caught
    /// per-subscriber inside Publish's loop, but only in Editor/Development builds - dispatch
    /// continues to later subscribers there. In release builds this isolation is compiled out
    /// entirely for performance: a mediator handler exception still propagates out of Publish and
    /// skips subsequent subscribers on that same publish call, exactly as before MSG-02. See
    /// MvcMessageBus.cs's class remarks and messaging.md's "Subscriber exceptions" section for the
    /// full rationale for this deliberate, permanent dev/release asymmetry.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public class CommandFailureContainmentScenarioTests
    {
        private sealed class NoInitModule : MvcModule
        {
            protected override void Awake() { }
            protected override void OnDestroy() { }
        }

        private readonly struct FailureMessage : IMessage { }

        private sealed class ThrowingCommand : Command
        {
            public static int ExecuteAttempts;
            public override void Execute()
            {
                ExecuteAttempts++;
                throw new InvalidOperationException("intentional command failure");
            }
        }

        private MvcDiContainer _container;
        private MvcMessageBus _bus;
        private MvcCommandProcessor _processor;
        private GameObject _moduleGo;

        [SetUp]
        public void SetUp()
        {
            ThrowingCommand.ExecuteAttempts = 0;
            _moduleGo = new GameObject(nameof(CommandFailureContainmentScenarioTests));
            var module = _moduleGo.AddComponent<NoInitModule>();
            _container = new MvcDiContainer();
            _bus = new MvcMessageBus();
            _processor = new MvcCommandProcessor(typeof(NoInitModule), _container, _bus, module);
        }

        [TearDown]
        public void TearDown()
        {
            _processor?.Dispose();
            _bus?.Dispose();
            _container?.Dispose();
            if (_moduleGo != null) Object.DestroyImmediate(_moduleGo);
        }

        [Test]
        public void SyncCommandThrows_ProcessorCatchesIt_OnCommandFaultFiresAndDispatchContinues()
        {
            var faults = new List<Type>();
            void OnFault(Type commandType, Exception ex) => faults.Add(commandType);
            MvcCommandProcessor.OnCommandFault += OnFault;

            var laterHandlerRan = false;
            try
            {
                _processor.BindCommand<ThrowingCommand, FailureMessage>();
                _bus.Subscribe<FailureMessage>(() => laterHandlerRan = true);

                LogAssert.ignoreFailingMessages = true;
                Assert.DoesNotThrow(() => _bus.Publish<FailureMessage>(),
                    "A throwing sync command must not propagate its exception out of Publish - " +
                    "MvcCommandProcessor catches it at the ExecuteCommandDirect0 catch block.");
            }
            finally
            {
                MvcCommandProcessor.OnCommandFault -= OnFault;
                LogAssert.ignoreFailingMessages = false;
            }

            Assert.That(ThrowingCommand.ExecuteAttempts, Is.EqualTo(1),
                "The command must have actually executed (and thrown) once.");
            Assert.That(faults, Is.EquivalentTo(new[] { typeof(ThrowingCommand) }),
                "OnCommandFault must fire exactly once with the failing command's type.");
            Assert.That(laterHandlerRan, Is.True,
                "A bus subscriber registered after the command binding must still run on the same " +
                "publish, since the command's exception was contained by the processor and never " +
                "reached the bus's dispatch loop.");
        }

        [Test]
        public void MediatorHandlerThrows_IsolatedInDevBuilds_LaterSubscriberStillRunsOnSamePublish()
        {
            var firstHandlerRan = false;
            var laterHandlerRan = false;

            _bus.Subscribe<FailureMessage>(() => firstHandlerRan = true);
            _bus.Subscribe<FailureMessage>(() => throw new InvalidOperationException("intentional handler failure"));
            _bus.Subscribe<FailureMessage>(() => laterHandlerRan = true);

            LogAssert.Expect(LogType.Error, new Regex("Subscriber '.*' threw on FailureMessage:.*intentional handler failure"));

            Assert.DoesNotThrow(() => _bus.Publish<FailureMessage>(),
                "In Editor/Development builds, Publish now catches each subscriber's exception " +
                "individually (MSG-02) - it must not propagate out of Publish itself here.");

            Assert.That(firstHandlerRan, Is.True,
                "Handlers subscribed before the throwing one, in the same publish, must still run.");
            Assert.That(laterHandlerRan, Is.True,
                "Handlers subscribed after the throwing one must ALSO still run in Editor/Development " +
                "builds - the exception is isolated per-subscriber, not propagated. (This isolation is " +
                "compiled out entirely in release builds for performance, restoring the old fail-fast " +
                "behavior there - see the class remarks above.)");
        }
    }
}
