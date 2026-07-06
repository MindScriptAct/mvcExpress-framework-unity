using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests
{
    [TestFixture]
    public class CommandAsyncCancellationDispatchTests
    {
        // Exposes the protected Commander surface for direct-run tests.
        private class CommandRunnerTestModule : MvcModule
        {
            public Task RunPausableCancelCommandAsync() => Commander.RunAsync<PausableCancelCommand>();
            public Task RunUnrelatedThrowCommandAsync() => Commander.RunAsync<UnrelatedThrowCommand>();
        }

        // Suspends until released, then throws using its own CancelToken - simulates a real
        // in-flight command reacting to genuine module teardown (the token it captured at
        // Initialize() time really was cancelled by the time it resumes).
        private class PausableCancelCommand : CommandAsync
        {
            public static TaskCompletionSource<bool> Gate;

            public override async Task ExecuteAsync()
            {
                await Gate.Task;
                CancelToken.ThrowIfCancellationRequested();
            }
        }

        // Throws OperationCanceledException synchronously with no relationship to any real
        // cancelled token (the module is never destroyed in this test) - simulates an author's
        // own unrelated cancellation source, e.g. a separate timeout token. This must still be
        // visible as an error, not silently swallowed as if it were module teardown.
        private class UnrelatedThrowCommand : CommandAsync
        {
            public static bool ExecuteWasCalled;

            public override Task ExecuteAsync()
            {
                ExecuteWasCalled = true;
                throw new OperationCanceledException();
            }
        }

        private GameObject _moduleGo;

        [SetUp]
        public void SetUp()
        {
            PausableCancelCommand.Gate = new TaskCompletionSource<bool>();
            UnrelatedThrowCommand.ExecuteWasCalled = false;
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
        public void RunAsync_GenuineModuleCancellation_RunsQuietlyWithNoErrorLog()
        {
            _moduleGo = new GameObject("TestModule");
            var module = _moduleGo.AddComponent<CommandRunnerTestModule>();

            // Starts synchronously up to `await Gate.Task`, then returns an incomplete Task -
            // the command is suspended with its CancelToken already captured (Initialize() has
            // already run, exactly like a real in-flight command awaiting I/O).
            var runTask = module.RunPausableCancelCommandAsync();

            // Destroy the module while the command is still suspended - this is the real
            // cancellation trigger the whole feature exists for.
            Object.DestroyImmediate(_moduleGo);
            _moduleGo = null; // prevent TearDown from double-destroying

            // Resume the command now that its captured token has genuinely been cancelled.
            PausableCancelCommand.Gate.SetResult(true);

            // No Assert.Throws needed: the dispatch's internal try/catch swallows the exception
            // either way. What this test actually proves is which catch handled it - if the
            // general catch (Exception ex) => MvcDebug.LogError(...) path ran instead of the
            // quiet cancellation-specific one, Unity's test runner fails this test automatically
            // due to the unexpected error log.
            runTask.GetAwaiter().GetResult();
        }

        [Test]
        public void RunAsync_UnrelatedOperationCanceled_StillLogsAsError()
        {
            _moduleGo = new GameObject("TestModule2");
            var module = _moduleGo.AddComponent<CommandRunnerTestModule>();

            // The module is never destroyed here, so this command's CancelToken was never
            // cancelled - the `when` guard on the dispatch's cancellation catch must NOT match,
            // so this falls through to the general catch and logs as a normal error, exactly as
            // any other unexpected exception would.
            LogAssert.Expect(LogType.Error, new Regex("UnrelatedThrowCommand.*execution failed"));

            module.RunUnrelatedThrowCommandAsync().GetAwaiter().GetResult();

            Assert.IsTrue(UnrelatedThrowCommand.ExecuteWasCalled, "Command must actually have run.");
        }
    }
}
