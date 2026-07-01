using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using mvcExpress.Internal.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests.Scenarios
{
    /// <summary>
    /// Scenario tests for the real MvcModule initialization pipeline.
    /// These tests let Unity call MvcModule.Awake, then observe behavior through commands,
    /// mediators, lifecycle callbacks, and messages rather than reaching into internal state.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public class InitializationScenarioTests
    {
        private GameObject _moduleGo;

        private readonly struct InitMessage : IMessage { }
        private readonly struct InspectorResolveMessage : IMessage { }
        private readonly struct MissingProxyMessage : IMessage { }

        private static class Trace
        {
            public static readonly List<string> Events = new List<string>();

            public static bool InspectorServiceResolved;
            public static bool CodeServiceResolved;
            public static bool AttributeServiceResolved;
            public static bool MixedServicesResolved;
            public static bool ProxyInjectedIntoCommand;
            public static bool MissingProxyCommandExecuted;

            public static void Reset()
            {
                Events.Clear();
                InspectorServiceResolved = false;
                CodeServiceResolved = false;
                AttributeServiceResolved = false;
                MixedServicesResolved = false;
                ProxyInjectedIntoCommand = false;
                MissingProxyCommandExecuted = false;
            }
        }

        public sealed class InspectorInitServiceBehaviour : MonoBehaviour, IMvcLifecycle
        {
            public void OnInitialized() => Trace.Events.Add("service-inspector-init");
            public void OnCleanup() { }
        }

        public sealed class CodeInitService : IMvcLifecycle
        {
            public void OnInitialized() => Trace.Events.Add("service-code-init");
            public void OnCleanup() { }
        }

        [Register(typeof(FullSequenceModule), RegisterToLogic = true)]
        [Register(typeof(AttributeServiceModule), RegisterToLogic = true)]
        [Register(typeof(MixedRegistrationModule), RegisterToLogic = true)]
        [Register(typeof(ProxyBeforeCommandModule), RegisterToLogic = true)]
        public sealed class AttributeInitService : IMvcLifecycle
        {
            public void OnInitialized() => Trace.Events.Add("service-attribute-init");
            public void OnCleanup() { }
        }

        public sealed class CodeInitProxy : Proxy
        {
            [Inject] private CodeInitService _service;

            protected override void OnInitialized()
            {
                Assert.That(_service, Is.Not.Null,
                    "Services must be initialized and injectable before code proxies initialize.");
                Trace.Events.Add("proxy-code-init");
            }
        }

        [Register(typeof(FullSequenceModule), RegisterToLogic = true)]
        [Register(typeof(ProxyBeforeCommandModule), RegisterToLogic = true)]
        public sealed class AttributeInitProxy : Proxy
        {
            [Inject] private AttributeInitService _service;

            protected override void OnInitialized()
            {
                Assert.That(_service, Is.Not.Null,
                    "Attribute services must be injectable into attribute proxies during the proxy phase.");
                Trace.Events.Add("proxy-attribute-init");
            }
        }

        public sealed class FullSequenceCommand : Command
        {
            [Inject] private InspectorInitServiceBehaviour _inspectorService;
            [Inject] private AttributeInitService _attributeService;
            [Inject] private CodeInitService _codeService;
            [Inject] private AttributeInitProxy _attributeProxy;
            [Inject] private CodeInitProxy _codeProxy;

            public override void Execute()
            {
                Assert.That(_inspectorService, Is.Not.Null);
                Assert.That(_attributeService, Is.Not.Null);
                Assert.That(_codeService, Is.Not.Null);
                Assert.That(_attributeProxy, Is.Not.Null);
                Assert.That(_codeProxy, Is.Not.Null);
                Trace.Events.Add("command-full-execute");
            }
        }

        public sealed class InspectorResolveCommand : Command
        {
            [Inject] private InspectorInitServiceBehaviour _service;

            public override void Execute()
            {
                Assert.That(_service, Is.Not.Null,
                    "A service supplied by the Unity inspector registry should be injectable into commands.");
                Trace.InspectorServiceResolved = true;
            }
        }

        public sealed class CodeResolveCommand : Command
        {
            [Inject] private CodeInitService _service;

            public override void Execute()
            {
                Assert.That(_service, Is.Not.Null,
                    "A service registered in RegisterServices should be injectable into commands.");
                Trace.CodeServiceResolved = true;
            }
        }

        public sealed class AttributeResolveCommand : Command
        {
            [Inject] private AttributeInitService _service;

            public override void Execute()
            {
                Assert.That(_service, Is.Not.Null,
                    "A service registered through [Register] should be injectable into commands.");
                Trace.AttributeServiceResolved = true;
            }
        }

        public sealed class MixedResolveCommand : Command
        {
            [Inject] private InspectorInitServiceBehaviour _inspectorService;
            [Inject] private AttributeInitService _attributeService;
            [Inject] private CodeInitService _codeService;

            public override void Execute()
            {
                Assert.That(_inspectorService, Is.Not.Null);
                Assert.That(_attributeService, Is.Not.Null);
                Assert.That(_codeService, Is.Not.Null);
                Trace.MixedServicesResolved = true;
            }
        }

        public sealed class ProxyAwareCommand : Command
        {
            [Inject] private AttributeInitProxy _proxy;

            public override void Execute()
            {
                Assert.That(_proxy, Is.Not.Null,
                    "Command execution after initialization should resolve proxies registered before BindCommands.");
                Trace.ProxyInjectedIntoCommand = true;
            }
        }

        public sealed class MissingProxyCommand : Command
        {
            [Inject] private CodeInitProxy _missingProxy;

            public override void Execute()
            {
                Trace.MissingProxyCommandExecuted = true;
            }
        }

        public sealed class TraceMediator : MediatorBehaviour
        {
            protected override void OnInitialized()
            {
                Trace.Events.Add("mediator-init");
                Messenger.Subscribe<InitMessage>(OnInitMessage);
            }

            private void OnInitMessage()
            {
                Trace.Events.Add("mediator-received");
            }
        }

        public sealed class FullSequenceModule : MvcModule
        {
            protected override void RegisterServices()
            {
                Trace.Events.Add("register-services-code");
                Container.Register(new CodeInitService()).ToLogic().AsPersistent();
            }

            protected override void RegisterProxies()
            {
                Trace.Events.Add("register-proxies-code");
                Container.Register(new CodeInitProxy()).ToLogic().AsPersistent();
            }

            protected override void BindCommands()
            {
                Trace.Events.Add("bind-commands-code");
                Commander.Bind<FullSequenceCommand, InitMessage>();
            }

            protected override void AttachMediators()
            {
                Trace.Events.Add("attach-mediators-code");
                var go = new GameObject("TraceMediator");
                go.transform.SetParent(transform);
                MediatorHub.Attach(go.AddComponent<TraceMediator>());
            }

            protected override void OnInitialized()
            {
                Trace.Events.Add("module-initialized");
                Messenger.Publish<InitMessage>();
            }
        }

        public sealed class InspectorServiceModule : MvcModule
        {
            protected override void OnInitialized()
            {
                Commander.Run<InspectorResolveCommand>();
            }
        }

        public sealed class CodeServiceModule : MvcModule
        {
            protected override void RegisterServices()
            {
                Container.Register(new CodeInitService()).ToLogic().AsPersistent();
            }

            protected override void OnInitialized()
            {
                Commander.Run<CodeResolveCommand>();
            }
        }

        public sealed class AttributeServiceModule : MvcModule
        {
            protected override void OnInitialized()
            {
                Commander.Run<AttributeResolveCommand>();
            }
        }

        public sealed class MixedRegistrationModule : MvcModule
        {
            protected override void RegisterServices()
            {
                Container.Register(new CodeInitService()).ToLogic().AsPersistent();
            }

            protected override void OnInitialized()
            {
                Commander.Run<MixedResolveCommand>();
            }
        }

        public sealed class ProxyBeforeCommandModule : MvcModule
        {
            protected override void BindCommands()
            {
                Commander.Bind<ProxyAwareCommand, InitMessage>();
            }

            protected override void OnInitialized()
            {
                Messenger.Publish<InitMessage>();
            }
        }

        public sealed class MissingProxyModule : MvcModule
        {
            protected override void BindCommands()
            {
                Commander.Bind<MissingProxyCommand, MissingProxyMessage>();
            }

            protected override void OnInitialized()
            {
                Messenger.Publish<MissingProxyMessage>();
            }
        }

        [SetUp]
        public void SetUp()
        {
            Trace.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            if (_moduleGo != null)
            {
                Object.DestroyImmediate(_moduleGo);
                _moduleGo = null;
            }

            var facade = MvcFacade.InstanceOrNull;
            if (facade != null)
            {
                Object.DestroyImmediate(facade.gameObject);
            }
        }

        [Test]
        public void FullModuleInitializationSequence_AllThreeRegistrationPaths_AllActorsInitializeInCorrectOrder()
        {
            CreateModuleWithInspectorService<FullSequenceModule>();

            AssertInOrder("service-inspector-init", "service-attribute-init", "service-code-init");
            AssertInOrder("service-code-init", "proxy-attribute-init", "proxy-code-init");
            AssertInOrder("proxy-code-init", "bind-commands-code");
            AssertInOrder("bind-commands-code", "mediator-init");
            AssertInOrder("mediator-init", "module-initialized");
            AssertInOrder("module-initialized", "command-full-execute", "mediator-received");
        }

        [Test]
        public void FullModuleInitializationSequence_ServicesBeforeProxies_OrderEnforced()
        {
            CreateModuleWithInspectorService<FullSequenceModule>();

            AssertInOrder("service-inspector-init", "proxy-attribute-init");
            AssertInOrder("service-attribute-init", "proxy-attribute-init");
            AssertInOrder("service-code-init", "proxy-code-init");
        }

        [Test]
        public void FullModuleInitializationSequence_ProxiesBeforeCommands_OrderEnforced()
        {
            CreateModuleWithInspectorService<FullSequenceModule>();

            AssertInOrder("proxy-attribute-init", "bind-commands-code");
            AssertInOrder("proxy-code-init", "bind-commands-code");
            AssertInOrder("bind-commands-code", "command-full-execute");
        }

        [Test]
        public void FullModuleInitializationSequence_CommandsBeforeMediators_OrderEnforced()
        {
            CreateModuleWithInspectorService<FullSequenceModule>();

            AssertInOrder("bind-commands-code", "attach-mediators-code", "mediator-init");
        }

        [Test]
        public void UnityInspectorRegistrationPath_ServiceRegistered_ResolvableFromCommand()
        {
            CreateModuleWithInspectorService<InspectorServiceModule>();

            Assert.That(Trace.InspectorServiceResolved, Is.True,
                "A MonoBehaviour service from ServiceRegistryBehaviour should be registered before OnInitialized command execution.");
        }

        [Test]
        public void CodeRegistrationPath_ServiceRegistered_ResolvableFromCommand()
        {
            CreateModule<CodeServiceModule>();

            Assert.That(Trace.CodeServiceResolved, Is.True,
                "A code-registered service should be available for command injection by OnInitialized.");
        }

        [Test]
        public void AttributeRegistrationPath_ServiceRegistered_ResolvableFromCommand()
        {
            CreateModule<AttributeServiceModule>();

            Assert.That(Trace.AttributeServiceResolved, Is.True,
                "An attribute-registered service should be available for command injection by OnInitialized.");
        }

        [Test]
        public void MixedRegistration_AllThreePaths_AllServicesResolvable()
        {
            CreateModuleWithInspectorService<MixedRegistrationModule>();

            Assert.That(Trace.MixedServicesResolved, Is.True,
                "Inspector, attribute, and code-registered services should all be present before post-init command orchestration.");
        }

        [Test]
        public void RegistrationOrderConstraint_ProxyBeforeCommandBinding_Succeeds()
        {
            CreateModule<ProxyBeforeCommandModule>();

            Assert.That(Trace.ProxyInjectedIntoCommand, Is.True,
                "Proxy registration and initialization should complete before a bound command runs from an OnInitialized publish.");
        }

        [Test]
        public void RegistrationOrderConstraint_CommandBindingBeforeProxy_ThrowsOrDefers()
        {
            LogAssert.Expect(LogType.Error, new Regex("Command 'MissingProxyCommand' execution failed"));

            CreateModule<MissingProxyModule>();

            Assert.That(Trace.MissingProxyCommandExecuted, Is.False,
                "A command whose required proxy was never registered should fail during injection and never reach Execute.");
        }

        private void CreateModule<TModule>() where TModule : MvcModule
        {
            _moduleGo = new GameObject(typeof(TModule).Name);
            _moduleGo.AddComponent<TModule>();
        }

        private void CreateModuleWithInspectorService<TModule>() where TModule : MvcModule
        {
            _moduleGo = new GameObject(typeof(TModule).Name);

            var servicesGo = new GameObject("Services");
            servicesGo.transform.SetParent(_moduleGo.transform);
            var registry = servicesGo.AddComponent<ServiceRegistryBehaviour>();
            var service = servicesGo.AddComponent<InspectorInitServiceBehaviour>();
            SetPrivateField(registry, "_serviceMappings", new[]
            {
                new ServiceMapping
                {
                    Service = service,
                    RegisterToLogic = true,
                    RegisterToView = true,
                    IsTransient = false
                }
            });

            _moduleGo.AddComponent<TModule>();
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Expected serialized field '{0}' on {1}.", fieldName, target.GetType().Name);
            field.SetValue(target, value);
        }

        private static void AssertInOrder(params string[] events)
        {
            var previousIndex = -1;
            foreach (var evt in events)
            {
                var index = Trace.Events.IndexOf(evt);
                Assert.That(index, Is.GreaterThan(previousIndex),
                    "Expected event '{0}' after index {1}. Trace: {2}", evt, previousIndex, string.Join(", ", Trace.Events));
                previousIndex = index;
            }
        }
    }
}




