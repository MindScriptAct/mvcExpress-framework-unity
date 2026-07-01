using mvcExpress;

namespace mvcExpress.Tests.Fakes
{
    public struct TestMessage : IMessage {}
    public struct TestMessageA : IMessage {}
    public struct TestMessageB : IMessage {}
    public struct TestMessageC : IMessage {}

    public struct TestMessage1<T1> : IMessage<T1> { public T1 Value; }
    public struct IntMessage : IMessage<int> { public int Value; }
    public struct StringMessage : IMessage<string> { public string Value; }
}
