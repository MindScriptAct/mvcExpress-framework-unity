using mvcExpress;

namespace mvcExpress.Tests.Fakes
{
    public class SpyCommand : Command
    {
        public int ExecuteCount { get; private set; }

        public override void Execute() => ExecuteCount++;

        public void Reset() => ExecuteCount = 0;
    }

    public class SpyCommand<T> : Command<T>
    {
        public int ExecuteCount { get; private set; }
        public T LastPayload { get; private set; }

        public override void Execute(T p1)
        {
            ExecuteCount++;
            LastPayload = p1;
        }
    }
}
