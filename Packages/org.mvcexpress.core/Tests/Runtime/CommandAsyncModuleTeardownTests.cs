using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests
{
    [TestFixture]
    public class CommandAsyncModuleTeardownTests
    {
        private class CommandRunnerTestModule : MvcModule
        {
            public Task RunPausableCommandAsync() => Commander.RunAsync<PausableCommand>();
        }

        private class PausableCommand : CommandAsync
        {
            public static TaskCompletionSource<bool> Gate;
            public static bool ObservedCancellation;

            public override async Task ExecuteAsync()
            {
                await Gate.Task;
                ObservedCancellation = CancelToken.IsCancellationRequested;
            }
        }

        private GameObject _moduleGo;

        [SetUp]
        public void SetUp()
        {
            PausableCommand.Gate = new TaskCompletionSource<bool>();
            PausableCommand.ObservedCancellation = false;
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
        public void CommandSuspendedMidAwait_ModuleDestroyedThenGateReleased_ObservesCancellation()
        {
            _moduleGo = new GameObject("TestModule");
            var module = _moduleGo.AddComponent<CommandRunnerTestModule>();

            // Starts executing synchronously up to `await Gate.Task`, then returns an
            // incomplete Task here - the command is now suspended, exactly like a real
            // in-flight CommandAsync awaiting I/O when its module gets torn down.
            var runTask = module.RunPausableCommandAsync();

            // Destroy the module while the command is still suspended.
            Object.DestroyImmediate(_moduleGo);
            _moduleGo = null; // prevent TearDown from double-destroying

            // Resume the suspended command now that the module (and its token) are gone.
            PausableCommand.Gate.SetResult(true);
            runTask.GetAwaiter().GetResult();

            Assert.IsTrue(PausableCommand.ObservedCancellation);
        }
    }
}
