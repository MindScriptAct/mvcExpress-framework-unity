using System.Threading.Tasks;

namespace mvcExpress
{
    /// <summary>
    /// Asynchronous command for operations that require no payload from the triggering message
    /// and need to await I/O, coroutines, or other async work.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="Command"/> but returns a <c>Task</c> from <see cref="ExecuteAsync"/>,
    /// allowing the framework to await the operation before proceeding. The same lifecycle applies:
    /// the framework injects dependencies, calls <c>OnInitialize()</c>, and then calls
    /// <c>ExecuteAsync()</c>.
    ///
    /// Use <c>CommandAsync</c> when:
    /// <list type="bullet">
    ///   <item>The operation awaits I/O, <c>UniTask</c>, or a Unity coroutine result.</item>
    ///   <item>No payload needs to be forwarded from the triggering message; use
    ///   <see cref="CommandAsync{T1}"/> and its arity variants for parametrised cases.</item>
    /// </list>
    ///
    /// Prefer <see cref="Command"/> for purely synchronous work - async dispatch carries a
    /// <c>Task</c> allocation per execution that the sync path avoids.
    ///
    /// Bind in your module's <c>BindCommands()</c> phase:
    /// <code>
    /// BindCommand&lt;MyAsyncCommand, MyMessage&gt;();
    /// </code>
    /// </remarks>
    public abstract class CommandAsync : MvcAsyncCommandBase
    {
        /// <summary>
        /// Implements the command's single-responsibility asynchronous operation.
        /// </summary>
        /// <returns>
        /// A <c>Task</c> that the framework awaits before the dispatch pipeline continues.
        /// </returns>
        /// <remarks>
        /// Called by the framework after dependency injection and <c>OnInitialize()</c>.
        /// Do not call this directly - dispatch through the message bus or <c>Commander</c>.
        /// </remarks>
        public abstract Task ExecuteAsync();
    }

    /// <summary>
    /// Asynchronous command with one payload value. Override <c>ExecuteAsync(T1)</c> with your operation.
    /// </summary>
    /// <typeparam name="T1">Type of the first payload parameter passed to <see cref="ExecuteAsync"/>.</typeparam>
    public abstract class CommandAsync<T1> : MvcAsyncCommandBase
    {
        /// <summary>
        /// Implements the command's asynchronous operation using the supplied payload.
        /// </summary>
        /// <param name="p1">First payload value forwarded from the triggering message or direct call.</param>
        /// <returns>A <c>Task</c> that the framework awaits before the dispatch pipeline continues.</returns>
        public abstract Task ExecuteAsync(T1 p1);
    }

    /// <summary>
    /// Asynchronous command with two payload values. Override <c>ExecuteAsync(T1, T2)</c> with your operation.
    /// </summary>
    /// <typeparam name="T1">Type of the first payload parameter.</typeparam>
    /// <typeparam name="T2">Type of the second payload parameter.</typeparam>
    public abstract class CommandAsync<T1, T2> : MvcAsyncCommandBase
    {
        /// <summary>Implements the command's asynchronous operation using the supplied payload.</summary>
        /// <param name="p1">First payload value.</param>
        /// <param name="p2">Second payload value.</param>
        /// <returns>A <c>Task</c> that the framework awaits before the dispatch pipeline continues.</returns>
        public abstract Task ExecuteAsync(T1 p1, T2 p2);
    }

    /// <summary>
    /// Asynchronous command with three payload values. Override <c>ExecuteAsync(T1, T2, T3)</c> with your operation.
    /// </summary>
    /// <typeparam name="T1">Type of the first payload parameter.</typeparam>
    /// <typeparam name="T2">Type of the second payload parameter.</typeparam>
    /// <typeparam name="T3">Type of the third payload parameter.</typeparam>
    public abstract class CommandAsync<T1, T2, T3> : MvcAsyncCommandBase
    {
        /// <summary>Implements the command's asynchronous operation using the supplied payload.</summary>
        /// <param name="p1">First payload value.</param>
        /// <param name="p2">Second payload value.</param>
        /// <param name="p3">Third payload value.</param>
        /// <returns>A <c>Task</c> that the framework awaits before the dispatch pipeline continues.</returns>
        public abstract Task ExecuteAsync(T1 p1, T2 p2, T3 p3);
    }

    /// <summary>
    /// Asynchronous command with four payload values. Override <c>ExecuteAsync(T1..T4)</c> with your operation.
    /// </summary>
    /// <typeparam name="T1">Type of the first payload parameter.</typeparam>
    /// <typeparam name="T2">Type of the second payload parameter.</typeparam>
    /// <typeparam name="T3">Type of the third payload parameter.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload parameter.</typeparam>
    public abstract class CommandAsync<T1, T2, T3, T4> : MvcAsyncCommandBase
    {
        /// <summary>Implements the command's asynchronous operation using the supplied payload.</summary>
        /// <param name="p1">First payload value.</param>
        /// <param name="p2">Second payload value.</param>
        /// <param name="p3">Third payload value.</param>
        /// <param name="p4">Fourth payload value.</param>
        /// <returns>A <c>Task</c> that the framework awaits before the dispatch pipeline continues.</returns>
        public abstract Task ExecuteAsync(T1 p1, T2 p2, T3 p3, T4 p4);
    }

    /// <summary>
    /// Asynchronous command with five payload values. Override <c>ExecuteAsync(T1..T5)</c> with your operation.
    /// </summary>
    /// <typeparam name="T1">Type of the first payload parameter.</typeparam>
    /// <typeparam name="T2">Type of the second payload parameter.</typeparam>
    /// <typeparam name="T3">Type of the third payload parameter.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload parameter.</typeparam>
    /// <typeparam name="T5">Type of the fifth payload parameter.</typeparam>
    public abstract class CommandAsync<T1, T2, T3, T4, T5> : MvcAsyncCommandBase
    {
        /// <summary>Implements the command's asynchronous operation using the supplied payload.</summary>
        /// <param name="p1">First payload value.</param>
        /// <param name="p2">Second payload value.</param>
        /// <param name="p3">Third payload value.</param>
        /// <param name="p4">Fourth payload value.</param>
        /// <param name="p5">Fifth payload value.</param>
        /// <returns>A <c>Task</c> that the framework awaits before the dispatch pipeline continues.</returns>
        public abstract Task ExecuteAsync(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5);
    }

    /// <summary>
    /// Asynchronous command with six payload values. Override <c>ExecuteAsync(T1..T6)</c> with your operation.
    /// </summary>
    /// <typeparam name="T1">Type of the first payload parameter.</typeparam>
    /// <typeparam name="T2">Type of the second payload parameter.</typeparam>
    /// <typeparam name="T3">Type of the third payload parameter.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload parameter.</typeparam>
    /// <typeparam name="T5">Type of the fifth payload parameter.</typeparam>
    /// <typeparam name="T6">Type of the sixth payload parameter.</typeparam>
    public abstract class CommandAsync<T1, T2, T3, T4, T5, T6> : MvcAsyncCommandBase
    {
        /// <summary>Implements the command's asynchronous operation using the supplied payload.</summary>
        /// <param name="p1">First payload value.</param>
        /// <param name="p2">Second payload value.</param>
        /// <param name="p3">Third payload value.</param>
        /// <param name="p4">Fourth payload value.</param>
        /// <param name="p5">Fifth payload value.</param>
        /// <param name="p6">Sixth payload value.</param>
        /// <returns>A <c>Task</c> that the framework awaits before the dispatch pipeline continues.</returns>
        public abstract Task ExecuteAsync(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6);
    }

    /// <summary>
    /// Asynchronous command with seven payload values. Override <c>ExecuteAsync(T1..T7)</c> with your operation.
    /// </summary>
    /// <typeparam name="T1">Type of the first payload parameter.</typeparam>
    /// <typeparam name="T2">Type of the second payload parameter.</typeparam>
    /// <typeparam name="T3">Type of the third payload parameter.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload parameter.</typeparam>
    /// <typeparam name="T5">Type of the fifth payload parameter.</typeparam>
    /// <typeparam name="T6">Type of the sixth payload parameter.</typeparam>
    /// <typeparam name="T7">Type of the seventh payload parameter.</typeparam>
    public abstract class CommandAsync<T1, T2, T3, T4, T5, T6, T7> : MvcAsyncCommandBase
    {
        /// <summary>Implements the command's asynchronous operation using the supplied payload.</summary>
        /// <param name="p1">First payload value.</param>
        /// <param name="p2">Second payload value.</param>
        /// <param name="p3">Third payload value.</param>
        /// <param name="p4">Fourth payload value.</param>
        /// <param name="p5">Fifth payload value.</param>
        /// <param name="p6">Sixth payload value.</param>
        /// <param name="p7">Seventh payload value.</param>
        /// <returns>A <c>Task</c> that the framework awaits before the dispatch pipeline continues.</returns>
        public abstract Task ExecuteAsync(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7);
    }

    /// <summary>
    /// Asynchronous command with eight payload values. Override <c>ExecuteAsync(T1..T8)</c> with your operation.
    /// </summary>
    /// <typeparam name="T1">Type of the first payload parameter.</typeparam>
    /// <typeparam name="T2">Type of the second payload parameter.</typeparam>
    /// <typeparam name="T3">Type of the third payload parameter.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload parameter.</typeparam>
    /// <typeparam name="T5">Type of the fifth payload parameter.</typeparam>
    /// <typeparam name="T6">Type of the sixth payload parameter.</typeparam>
    /// <typeparam name="T7">Type of the seventh payload parameter.</typeparam>
    /// <typeparam name="T8">Type of the eighth payload parameter.</typeparam>
    public abstract class CommandAsync<T1, T2, T3, T4, T5, T6, T7, T8> : MvcAsyncCommandBase
    {
        /// <summary>Implements the command's asynchronous operation using the supplied payload.</summary>
        /// <param name="p1">First payload value.</param>
        /// <param name="p2">Second payload value.</param>
        /// <param name="p3">Third payload value.</param>
        /// <param name="p4">Fourth payload value.</param>
        /// <param name="p5">Fifth payload value.</param>
        /// <param name="p6">Sixth payload value.</param>
        /// <param name="p7">Seventh payload value.</param>
        /// <param name="p8">Eighth payload value.</param>
        /// <returns>A <c>Task</c> that the framework awaits before the dispatch pipeline continues.</returns>
        public abstract Task ExecuteAsync(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8);
    }

    /// <summary>
    /// Asynchronous command with nine payload values. Override <c>ExecuteAsync(T1..T9)</c> with your operation.
    /// </summary>
    /// <typeparam name="T1">Type of the first payload parameter.</typeparam>
    /// <typeparam name="T2">Type of the second payload parameter.</typeparam>
    /// <typeparam name="T3">Type of the third payload parameter.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload parameter.</typeparam>
    /// <typeparam name="T5">Type of the fifth payload parameter.</typeparam>
    /// <typeparam name="T6">Type of the sixth payload parameter.</typeparam>
    /// <typeparam name="T7">Type of the seventh payload parameter.</typeparam>
    /// <typeparam name="T8">Type of the eighth payload parameter.</typeparam>
    /// <typeparam name="T9">Type of the ninth payload parameter.</typeparam>
    public abstract class CommandAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9> : MvcAsyncCommandBase
    {
        /// <summary>Implements the command's asynchronous operation using the supplied payload.</summary>
        /// <param name="p1">First payload value.</param>
        /// <param name="p2">Second payload value.</param>
        /// <param name="p3">Third payload value.</param>
        /// <param name="p4">Fourth payload value.</param>
        /// <param name="p5">Fifth payload value.</param>
        /// <param name="p6">Sixth payload value.</param>
        /// <param name="p7">Seventh payload value.</param>
        /// <param name="p8">Eighth payload value.</param>
        /// <param name="p9">Ninth payload value.</param>
        /// <returns>A <c>Task</c> that the framework awaits before the dispatch pipeline continues.</returns>
        public abstract Task ExecuteAsync(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9);
    }

    /// <summary>
    /// Asynchronous command with ten payload values. Override <c>ExecuteAsync(T1..T10)</c> with your operation.
    /// </summary>
    /// <typeparam name="T1">Type of the first payload parameter.</typeparam>
    /// <typeparam name="T2">Type of the second payload parameter.</typeparam>
    /// <typeparam name="T3">Type of the third payload parameter.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload parameter.</typeparam>
    /// <typeparam name="T5">Type of the fifth payload parameter.</typeparam>
    /// <typeparam name="T6">Type of the sixth payload parameter.</typeparam>
    /// <typeparam name="T7">Type of the seventh payload parameter.</typeparam>
    /// <typeparam name="T8">Type of the eighth payload parameter.</typeparam>
    /// <typeparam name="T9">Type of the ninth payload parameter.</typeparam>
    /// <typeparam name="T10">Type of the tenth payload parameter.</typeparam>
    public abstract class CommandAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : MvcAsyncCommandBase
    {
        /// <summary>Implements the command's asynchronous operation using the supplied payload.</summary>
        /// <param name="p1">First payload value.</param>
        /// <param name="p2">Second payload value.</param>
        /// <param name="p3">Third payload value.</param>
        /// <param name="p4">Fourth payload value.</param>
        /// <param name="p5">Fifth payload value.</param>
        /// <param name="p6">Sixth payload value.</param>
        /// <param name="p7">Seventh payload value.</param>
        /// <param name="p8">Eighth payload value.</param>
        /// <param name="p9">Ninth payload value.</param>
        /// <param name="p10">Tenth payload value.</param>
        /// <returns>A <c>Task</c> that the framework awaits before the dispatch pipeline continues.</returns>
        public abstract Task ExecuteAsync(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10);
    }

    /// <summary>
    /// Asynchronous command with eleven payload values. Override <c>ExecuteAsync(T1..T11)</c> with your operation.
    /// </summary>
    /// <typeparam name="T1">Type of the first payload parameter.</typeparam>
    /// <typeparam name="T2">Type of the second payload parameter.</typeparam>
    /// <typeparam name="T3">Type of the third payload parameter.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload parameter.</typeparam>
    /// <typeparam name="T5">Type of the fifth payload parameter.</typeparam>
    /// <typeparam name="T6">Type of the sixth payload parameter.</typeparam>
    /// <typeparam name="T7">Type of the seventh payload parameter.</typeparam>
    /// <typeparam name="T8">Type of the eighth payload parameter.</typeparam>
    /// <typeparam name="T9">Type of the ninth payload parameter.</typeparam>
    /// <typeparam name="T10">Type of the tenth payload parameter.</typeparam>
    /// <typeparam name="T11">Type of the eleventh payload parameter.</typeparam>
    public abstract class CommandAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> : MvcAsyncCommandBase
    {
        /// <summary>Implements the command's asynchronous operation using the supplied payload.</summary>
        /// <param name="p1">First payload value.</param>
        /// <param name="p2">Second payload value.</param>
        /// <param name="p3">Third payload value.</param>
        /// <param name="p4">Fourth payload value.</param>
        /// <param name="p5">Fifth payload value.</param>
        /// <param name="p6">Sixth payload value.</param>
        /// <param name="p7">Seventh payload value.</param>
        /// <param name="p8">Eighth payload value.</param>
        /// <param name="p9">Ninth payload value.</param>
        /// <param name="p10">Tenth payload value.</param>
        /// <param name="p11">Eleventh payload value.</param>
        /// <returns>A <c>Task</c> that the framework awaits before the dispatch pipeline continues.</returns>
        public abstract Task ExecuteAsync(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10, T11 p11);
    }

    /// <summary>
    /// Asynchronous command with twelve payload values. Override <c>ExecuteAsync(T1..T12)</c> with your operation.
    /// </summary>
    /// <remarks>
    /// If you need more than twelve parameters, consider grouping related values into a struct or
    /// passing a single message type that carries all the data.
    /// </remarks>
    /// <typeparam name="T1">Type of the first payload parameter.</typeparam>
    /// <typeparam name="T2">Type of the second payload parameter.</typeparam>
    /// <typeparam name="T3">Type of the third payload parameter.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload parameter.</typeparam>
    /// <typeparam name="T5">Type of the fifth payload parameter.</typeparam>
    /// <typeparam name="T6">Type of the sixth payload parameter.</typeparam>
    /// <typeparam name="T7">Type of the seventh payload parameter.</typeparam>
    /// <typeparam name="T8">Type of the eighth payload parameter.</typeparam>
    /// <typeparam name="T9">Type of the ninth payload parameter.</typeparam>
    /// <typeparam name="T10">Type of the tenth payload parameter.</typeparam>
    /// <typeparam name="T11">Type of the eleventh payload parameter.</typeparam>
    /// <typeparam name="T12">Type of the twelfth payload parameter.</typeparam>
    public abstract class CommandAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> : MvcAsyncCommandBase
    {
        /// <summary>Implements the command's asynchronous operation using the supplied payload.</summary>
        /// <param name="p1">First payload value.</param>
        /// <param name="p2">Second payload value.</param>
        /// <param name="p3">Third payload value.</param>
        /// <param name="p4">Fourth payload value.</param>
        /// <param name="p5">Fifth payload value.</param>
        /// <param name="p6">Sixth payload value.</param>
        /// <param name="p7">Seventh payload value.</param>
        /// <param name="p8">Eighth payload value.</param>
        /// <param name="p9">Ninth payload value.</param>
        /// <param name="p10">Tenth payload value.</param>
        /// <param name="p11">Eleventh payload value.</param>
        /// <param name="p12">Twelfth payload value.</param>
        /// <returns>A <c>Task</c> that the framework awaits before the dispatch pipeline continues.</returns>
        public abstract Task ExecuteAsync(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10, T11 p11, T12 p12);
    }
}
