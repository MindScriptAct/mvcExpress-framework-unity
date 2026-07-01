using System;
using mvcExpress.Internal.Commands;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests.Scenarios
{
    /// <summary>
    /// Scenario tests for real MvcFacade global scope.
    /// Each test creates the facade singleton, registers global dependencies on its container,
    /// and runs commands from independent module harnesses to prove the separation between
    /// application-wide global scope and module-local DI scope.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public class GlobalScopeScenarioTests
    {
        private MvcFacade _facade;
        private ModuleHarness _moduleA;
        private ModuleHarness _moduleB;

        private sealed class NoInitModule : MvcModule
        {
            protected override void Awake() { }
            protected override void OnDestroy() { }
        }

        private sealed class ModuleHarness : IDisposable
        {
            public readonly GameObject GameObject;
            public readonly NoInitModule Module;
            public readonly MvcDiContainer Container;
            public readonly MvcMessageBus MessageBus;
            public readonly MvcCommandProcessor Processor;

            public ModuleHarness(string name)
            {
                GameObject = new GameObject(name);
                Module = GameObject.AddComponent<NoInitModule>();
                Container = new MvcDiContainer();
                MessageBus = new MvcMessageBus();
                Processor = new MvcCommandProcessor(typeof(NoInitModule), Container, MessageBus, Module);
            }

            public void Dispose()
            {
                Processor.Dispose();
                MessageBus.Dispose();
                Container.Dispose();

                if (GameObject != null)
                {
                    Object.DestroyImmediate(GameObject);
                }
            }
        }

        private sealed class GlobalScenarioService
        {
            public readonly string Name;

            public GlobalScenarioService(string name)
            {
                Name = name;
            }
        }

        private sealed class LocalScenarioService
        {
            public readonly string Name;

            public LocalScenarioService(string name)
            {
                Name = name;
            }
        }

        private sealed class GlobalScenarioProxy : Proxy
        {
            public int Value;
        }

        private sealed class RegisterGlobalServiceCommand : Command
        {
            public static string ServiceName;

            public override void Execute()
            {
                Global.Register(new GlobalScenarioService(ServiceName)).ToLogic().AsPersistent();
            }
        }

        private sealed class RegisterGlobalProxyCommand : Command
        {
            public static GlobalScenarioProxy Proxy;

            public override void Execute()
            {
                Global.Register(Proxy).ToLogic().AsPersistent();
            }
        }

        private sealed class GlobalServiceCommand : Command
        {
            public static int ExecuteCount;
            public static string LastServiceName;

            [InjectGlobal] private GlobalScenarioService _service;

            public static void Reset()
            {
                ExecuteCount = 0;
                LastServiceName = null;
            }

            public override void Execute()
            {
                ExecuteCount++;
                Assert.That(_service, Is.Not.Null,
                    "[InjectGlobal] should resolve from MvcFacade.Global rather than the module-local container.");
                LastServiceName = _service.Name;
            }
        }

        private sealed class GlobalProxyCommand : Command
        {
            public static int ExecuteCount;
            public static int LastValue;

            [InjectGlobal] private GlobalScenarioProxy _proxy;

            public static void Reset()
            {
                ExecuteCount = 0;
                LastValue = 0;
            }

            public override void Execute()
            {
                ExecuteCount++;
                _proxy.Value++;
                LastValue = _proxy.Value;
            }
        }

        private sealed class LocalServiceCommand : Command
        {
            [Inject] private LocalScenarioService _service;

            public override void Execute()
            {
                Assert.That(_service, Is.Not.Null,
                    "This command should only succeed when its own module-local container has LocalScenarioService.");
            }
        }

        [SetUp]
        public void SetUp()
        {
            RegisterGlobalServiceCommand.ServiceName = null;
            RegisterGlobalProxyCommand.Proxy = null;
            GlobalServiceCommand.Reset();
            GlobalProxyCommand.Reset();

            _facade = MvcFacade.FacadeInstance;
            _facade.ClearGlobalContainer();
            _moduleA = new ModuleHarness("GlobalScopeScenarioModuleA");
            _moduleB = new ModuleHarness("GlobalScopeScenarioModuleB");
        }

        [TearDown]
        public void TearDown()
        {
            _moduleA?.Dispose();
            _moduleB?.Dispose();

            if (_facade != null)
            {
                _facade.ClearGlobalContainer();
                Object.DestroyImmediate(_facade.gameObject);
                _facade = null;
            }
        }

        [Test]
        public void GlobalService_RegisteredOnFacade_VisibleFromAllModules()
        {
            RegisterGlobalServiceCommand.ServiceName = "shared-service";
            _moduleA.Processor.Run<RegisterGlobalServiceCommand>();

            _moduleA.Processor.Run<GlobalServiceCommand>();
            Assert.That(GlobalServiceCommand.LastServiceName, Is.EqualTo("shared-service"),
                "Module A command should resolve the facade-registered global service through [InjectGlobal].");

            _moduleB.Processor.Run<GlobalServiceCommand>();
            Assert.That(GlobalServiceCommand.ExecuteCount, Is.EqualTo(2),
                "The same global registration should be visible to commands executing in multiple module contexts.");
            Assert.That(GlobalServiceCommand.LastServiceName, Is.EqualTo("shared-service"));
        }

        [Test]
        public void GlobalService_RegisteredOnFacade_NotVisibleInModuleLocalScope()
        {
            RegisterGlobalServiceCommand.ServiceName = "global-only";
            _moduleA.Processor.Run<RegisterGlobalServiceCommand>();

            Assert.Throws<InvalidOperationException>(() => _moduleA.Container.Resolve<GlobalScenarioService>(),
                "Global registrations should not appear in a module's local container. Local resolution and [Inject] remain module-scoped.");
        }

        [Test]
        public void GlobalProxy_RegisteredOnFacade_SharedAcrossModules()
        {
            var proxy = new GlobalScenarioProxy();
            RegisterGlobalProxyCommand.Proxy = proxy;
            _moduleA.Processor.Run<RegisterGlobalProxyCommand>();

            _moduleA.Processor.Run<GlobalProxyCommand>();
            _moduleB.Processor.Run<GlobalProxyCommand>();

            Assert.That(proxy.Value, Is.EqualTo(2),
                "Both modules should mutate the same facade-owned global proxy instance.");
            Assert.That(GlobalProxyCommand.LastValue, Is.EqualTo(2),
                "The second module execution should observe state already changed by the first module.");
        }

        [Test]
        public void CrossModuleInjection_GlobalServiceInjectedIntoModuleCommand_Succeeds()
        {
            RegisterGlobalServiceCommand.ServiceName = "cross-module";
            _moduleA.Processor.Run<RegisterGlobalServiceCommand>();

            Assert.DoesNotThrow(() => _moduleB.Processor.Run<GlobalServiceCommand>(),
                "A command in any module should be able to inject a facade-registered global service with [InjectGlobal].");
            Assert.That(GlobalServiceCommand.LastServiceName, Is.EqualTo("cross-module"));
        }

        [Test]
        public void CrossModuleInjection_ModuleLocalServiceNotInjectedIntoOtherModule_Fails()
        {
            _moduleA.Container.Register(new LocalScenarioService("A-only")).ToLogic().AsPersistent();

            Assert.DoesNotThrow(() => _moduleA.Processor.Run<LocalServiceCommand>(),
                "Precondition: the command should execute in the module that owns the local service.");
            Assert.Throws<InvalidOperationException>(() => _moduleB.Container.Resolve<LocalScenarioService>(),
                "Module B's local container should not see Module A's local service registration.");
        }
    }
}

