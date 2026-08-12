using System.Collections.Generic;
using System.Reflection;
using mvcExpress.Internal.Services;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests.Scenarios
{
    /// <summary>
    /// One module using all three registration styles at once, across every actor kind:
    /// Inspector-mapped service, [Register] attribute proxy, code-bound command, Inspector
    /// scene mediator (via MediatorRegistryBehaviour), and [Attach] attribute mediator. Guards
    /// the framework's headline "mix and match registration styles" feature end-to-end,
    /// extending the per-style and per-actor-pair coverage already in
    /// InitializationScenarioTests.cs.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public class MixedRegistrationStylesScenarioTests
    {
        private static class Trace
        {
            public static readonly List<string> Events = new List<string>();
            public static void Reset() => Events.Clear();
        }

        public sealed class InspectorMixedService : MonoBehaviour, IMvcLifecycle
        {
            public void OnInitialized() => Trace.Events.Add("service-inspector-init");
            public void OnCleanup() { }
        }

        [Register(typeof(MixedStylesModule), RegisterToLogic = true)]
        public sealed class AttributeMixedProxy : Proxy
        {
            protected override void OnInitialized() => Trace.Events.Add("proxy-attribute-init");
        }

        private readonly struct MixedInitMessage : IMessage { }

        public sealed class CodeMixedCommand : Command
        {
            public override void Execute() => Trace.Events.Add("command-code-execute");
        }

        public sealed class InspectorSceneMediator : MediatorBehaviour
        {
            protected override void OnInitialized() => Trace.Events.Add("mediator-inspector-init");
        }

        // [Attach] requires exactly one attachment strategy (FindInScene or PrefabPath); a bare
        // [Attach(typeof(Module))] with neither set logs a warning and never attaches
        // (see ModuleInitializer.AttachAttributeMediator, "has [Attach] attribute but no
        // PrefabPath or FindInScene flag"). FindInScene = true is the strategy that lets a
        // pre-existing scene instance (created below in the test) be picked up via
        // Object.FindObjectOfType during module initialization.
        [Attach(typeof(MixedStylesModule), FindInScene = true)]
        public sealed class AttributeAttachedMediator : MediatorBehaviour
        {
            protected override void OnInitialized() => Trace.Events.Add("mediator-attach-init");
        }

        public sealed class MixedStylesModule : MvcModule
        {
            protected override void BindCommands()
            {
                Commander.Bind<CodeMixedCommand, MixedInitMessage>();
            }

            protected override void OnInitialized()
            {
                Trace.Events.Add("module-initialized");
                Messenger.Publish<MixedInitMessage>();
            }
        }

        public interface IReadOnlyMixedThing { }
        public interface IMeasurementMixedThing { }

        [Register(typeof(EndToEndStackingModule), RegisterToLogic = true, LogicInterface = typeof(IReadOnlyMixedThing))]
        [Register(typeof(EndToEndStackingModule), RegisterToLogic = true, LogicInterface = typeof(IMeasurementMixedThing))]
        public sealed class EndToEndStackedProxy : Proxy, IReadOnlyMixedThing, IMeasurementMixedThing
        {
            protected override void OnInitialized() => Trace.Events.Add("stacked-proxy-init");
        }

        public sealed class EndToEndStackingModule : MvcModule { }

        private GameObject _moduleGo;
        private GameObject _attachedMediatorGo;

        [SetUp]
        public void SetUp()
        {
            Trace.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            if (_moduleGo != null) Object.DestroyImmediate(_moduleGo);
            if (_attachedMediatorGo != null) Object.DestroyImmediate(_attachedMediatorGo);
            var facade = MvcFacade.InstanceOrNull;
            if (facade != null) Object.DestroyImmediate(facade.gameObject);
        }

        [Test]
        public void OneModule_AllThreeStylesAcrossAllActorKinds_InitializesInOrderAndTeardownIsClean()
        {
            _moduleGo = new GameObject(nameof(MixedStylesModule));

            // Inspector-registered service, mirroring CreateModuleWithInspectorService in
            // InitializationScenarioTests.cs.
            var servicesGo = new GameObject("Services");
            servicesGo.transform.SetParent(_moduleGo.transform);
            var serviceRegistry = servicesGo.AddComponent<ServiceRegistryBehaviour>();
            var inspectorService = servicesGo.AddComponent<InspectorMixedService>();
            SetPrivateField(serviceRegistry, "_serviceMappings", new[]
            {
                new ServiceMapping
                {
                    Service = inspectorService,
                    RegisterToLogic = true,
                    RegisterToView = true,
                    IsTransient = false,
                }
            });

            // Inspector-registered scene mediator. MediatorRegistryBehaviour is the Unity-behaviour
            // registration method for mediators (parallel to ServiceRegistryBehaviour for services):
            // MvcModule.EnsureMvcContainers() auto-discovers a MediatorRegistryBehaviour on a child
            // named "View" (or anywhere in children) and reads its serialized _sceneMediators field
            // during the AttachMediators phase (see MediatorRegistryBehaviour.cs and
            // MediatorRegistrar.RegisterSerializedMediators()).
            var viewGo = new GameObject("View");
            viewGo.transform.SetParent(_moduleGo.transform);
            var mediatorRegistry = viewGo.AddComponent<MediatorRegistryBehaviour>();
            var inspectorMediator = viewGo.AddComponent<InspectorSceneMediator>();
            SetPrivateField(mediatorRegistry, "_sceneMediators", new MediatorBehaviour[] { inspectorMediator });

            // [Attach(FindInScene = true)] resolves via Object.FindObjectOfType, so the target
            // instance must already exist in the scene before the module initializes.
            _attachedMediatorGo = new GameObject("AttributeAttachedMediator");
            _attachedMediatorGo.AddComponent<AttributeAttachedMediator>();

            _moduleGo.AddComponent<MixedStylesModule>();

            Assert.That(Trace.Events, Does.Contain("service-inspector-init"));
            Assert.That(Trace.Events, Does.Contain("proxy-attribute-init"));
            Assert.That(Trace.Events, Does.Contain("mediator-inspector-init"));
            Assert.That(Trace.Events, Does.Contain("mediator-attach-init"));
            Assert.That(Trace.Events, Does.Contain("command-code-execute"));

            AssertInOrder("service-inspector-init", "proxy-attribute-init");
            AssertInOrder("proxy-attribute-init", "mediator-inspector-init", "mediator-attach-init");
            AssertInOrder("mediator-attach-init", "module-initialized");
            AssertInOrder("module-initialized", "command-code-execute");
        }

        [Test]
        public void StackedAttributeProxy_ThroughFullModuleBoot_ResolvesUnderBothInterfacesAsSameInstance()
        {
            _moduleGo = new GameObject(nameof(EndToEndStackingModule));
            var module = _moduleGo.AddComponent<EndToEndStackingModule>();

            Assert.That(Trace.Events, Does.Contain("stacked-proxy-init"));

            var asReadOnly = module.DiContainer.Resolve<IReadOnlyMixedThing>();
            var asMeasurement = module.DiContainer.Resolve<IMeasurementMixedThing>();
            Assert.That(asReadOnly, Is.SameAs(asMeasurement));
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
