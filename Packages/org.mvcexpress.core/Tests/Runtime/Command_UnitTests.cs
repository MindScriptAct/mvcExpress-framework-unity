using NUnit.Framework;
using mvcExpress;

namespace mvcExpress.Tests
{
    public class Command_UnitTests
    {
        private class TestCommand : Command
        {
            public bool WasExecuted { get; private set; }
            public override void Execute()
            {
                WasExecuted = true;
            }
        }

        private class TestCommand1 : Command<int>
        {
            public int ReceivedValue { get; private set; }
            public override void Execute(int p1)
            {
                ReceivedValue = p1;
            }
        }

        private class TestCommand2 : Command<int, string>
        {
            public int ReceivedInt { get; private set; }
            public string ReceivedString { get; private set; }
            public override void Execute(int p1, string p2)
            {
                ReceivedInt = p1;
                ReceivedString = p2;
            }
        }

        private class TestCommand12 : Command<int, int, int, int, int, int, int, int, int, int, int, int>
        {
            public int Sum { get; private set; }
            public override void Execute(int p1, int p2, int p3, int p4, int p5, int p6, int p7, int p8, int p9, int p10, int p11, int p12)
            {
                Sum = p1 + p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9 + p10 + p11 + p12;
            }
        }

        [Test]
        public void Execute_Parameterless_InvokesImplementation()
        {
            var cmd = new TestCommand();
            cmd.Execute();
            Assert.IsTrue(cmd.WasExecuted);
        }

        [Test]
        public void Execute_With1Parameter_ReceivesPayload()
        {
            var cmd = new TestCommand1();
            cmd.Execute(42);
            Assert.AreEqual(42, cmd.ReceivedValue);
        }

        [Test]
        public void Execute_With2Parameters_ReceivesPayloads()
        {
            var cmd = new TestCommand2();
            cmd.Execute(42, "test");
            Assert.AreEqual(42, cmd.ReceivedInt);
            Assert.AreEqual("test", cmd.ReceivedString);
        }

        [Test]
        public void Execute_With12Parameters_ReceivesPayloads()
        {
            var cmd = new TestCommand12();
            cmd.Execute(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
            Assert.AreEqual(78, cmd.Sum);
        }
    }
}
