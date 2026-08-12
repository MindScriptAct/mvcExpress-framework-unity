using System;
using mvcExpress.Internal.Commands;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests
{
    [TestFixture]
    public class MvcCommandProcessor_ArityValidation_UnitTests
    {
        private MvcDiContainer _container;
        private MvcMessageBus _messageBus;
        private MvcCommandProcessor _processor;
        private GameObject _moduleGo;

        private sealed class NoInitModule : MvcModule
        {
            protected override void Awake() { }
            protected override void OnDestroy() { }
        }

        private readonly struct OnePayloadMessage : IMessage<int> { }

        // Zero-payload command - deliberately mismatched against a one-payload message below.
        private sealed class ZeroPayloadCommand : Command
        {
            public override void Execute() { }
        }

        [SetUp]
        public void SetUp()
        {
            _moduleGo = new GameObject(nameof(MvcCommandProcessor_ArityValidation_UnitTests));
            var module = _moduleGo.AddComponent<NoInitModule>();
            _container = new MvcDiContainer();
            _messageBus = new MvcMessageBus();
            _processor = new MvcCommandProcessor(typeof(NoInitModule), _container, _messageBus, module);
        }

        [TearDown]
        public void TearDown()
        {
            _processor?.Dispose();
            _messageBus?.Dispose();
            _container?.Dispose();
            if (_moduleGo != null) Object.DestroyImmediate(_moduleGo);
        }

        [Test]
        public void BindCommand_ArityMismatchedAgainstMessage_ThrowsAtBindTime()
        {
            Assert.Throws<InvalidOperationException>(
                () => _processor.BindCommand<ZeroPayloadCommand, OnePayloadMessage, int>(),
                "Binding a zero-payload Command to a one-payload IMessage<int> must fail loudly at bind " +
                "time via ValidateCommandArity, not silently no-op at dispatch (the pre-fix behavior).");
        }
    }
}
