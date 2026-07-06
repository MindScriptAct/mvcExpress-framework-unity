using System;
using System.Threading.Tasks;
using NUnit.Framework;
using mvcExpress;

namespace mvcExpress.Tests
{
    // NOTE: async Task test methods with Task.Delay() deadlock Unity's main-thread SynchronizationContext.
    // The timer continuation needs to post back to the main thread, but the main thread is blocked
    // waiting for the test to complete. Fix: make all test commands complete synchronously,
    // and call ExecuteAsync().GetAwaiter().GetResult() to drive the test on the main thread.
    public class CommandAsync_UnitTests
    {
        private class TestCommandAsync : CommandAsync
        {
            public bool WasExecuted { get; private set; }
            public override Task ExecuteAsync()
            {
                WasExecuted = true;
                return Task.CompletedTask;
            }
        }

        private class TestCommandAsync1 : CommandAsync<int>
        {
            public int ReceivedValue { get; private set; }
            public override Task ExecuteAsync(int p1)
            {
                ReceivedValue = p1;
                return Task.CompletedTask;
            }
        }

        private class TestCommandAsyncException : CommandAsync
        {
            public override Task ExecuteAsync()
            {
                throw new InvalidOperationException("Async Error");
            }
        }

        [Test]
        public void ExecuteAsync_Parameterless_CompletesAndMutatesState()
        {
            var cmd = new TestCommandAsync();
            cmd.ExecuteAsync().GetAwaiter().GetResult();
            Assert.IsTrue(cmd.WasExecuted);
        }

        [Test]
        public void ExecuteAsync_With1Parameter_ReceivesPayload()
        {
            var cmd = new TestCommandAsync1();
            cmd.ExecuteAsync(42).GetAwaiter().GetResult();
            Assert.AreEqual(42, cmd.ReceivedValue);
        }

        [Test]
        public void ExecuteAsync_ThrowingException_PropagatesException()
        {
            var cmd = new TestCommandAsyncException();
            Assert.Throws<InvalidOperationException>(() => cmd.ExecuteAsync().GetAwaiter().GetResult());
        }
    }
}
