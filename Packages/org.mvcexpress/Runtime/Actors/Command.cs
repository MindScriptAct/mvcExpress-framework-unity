namespace mvcExpress
{
    /// <summary>
    /// Synchronous command for operations that require no payload from the triggering message.
    /// </summary>
    /// <remarks>
    /// Commands are the primary mechanism for isolating single-responsibility operations in
    /// mvcExpress. They are transient actors: the framework instantiates a new instance per
    /// execution (or reuses a pooled one when <c>poolSize</c> is set on the binding), injects
    /// all registered dependencies via <c>[Inject]</c> properties, calls <c>OnInitialize()</c>,
    /// and then calls <c>Execute()</c>. Commands must have a parameterless public constructor.
    ///
    /// Use <c>Command</c> when:
    /// <list type="bullet">
    ///   <item>The operation needs no data from the message bus trigger (zero-payload bind).</item>
    ///   <item>The operation is fully synchronous - prefer <see cref="CommandAsync"/> for
    ///   anything that awaits I/O or coroutines.</item>
    /// </list>
    ///
    /// Bind in your module's <c>BindCommands()</c> phase:
    /// <code>
    /// Commander.Bind&lt;MyCommand, MyMessage&gt;();
    /// </code>
    /// Or run directly via <c>Commander</c>:
    /// <code>
    /// Commander.Run&lt;MyCommand&gt;();
    /// </code>
    /// </remarks>
    public abstract class Command : MvcSyncCommandBase
    {
        /// <summary>
        /// Implements the command's single-responsibility operation.
        /// </summary>
        /// <remarks>
        /// Called by the framework after dependency injection and <c>OnInitialize()</c>.
        /// Do not call this directly - dispatch through the message bus or <c>Commander</c>.
        /// </remarks>
        public abstract void Execute();
    }

    /// <summary>
    /// Synchronous command with one payload value. Override <c>Execute(T1)</c> with your operation.
    /// </summary>
    /// <typeparam name="T1">Type of the first payload parameter passed to <see cref="Execute"/>.</typeparam>
    public abstract class Command<T1> : MvcSyncCommandBase
    {
        /// <summary>
        /// Implements the command's operation using the supplied payload.
        /// </summary>
        /// <param name="p1">First payload value forwarded from the triggering message or direct call.</param>
        public abstract void Execute(T1 p1);
    }

    /// <summary>
    /// Synchronous command with two payload values. Override <c>Execute(T1, T2)</c> with your operation.
    /// </summary>
    /// <typeparam name="T1">Type of the first payload parameter.</typeparam>
    /// <typeparam name="T2">Type of the second payload parameter.</typeparam>
    public abstract class Command<T1, T2> : MvcSyncCommandBase
    {
        /// <summary>
        /// Implements the command's operation using the supplied payload.
        /// </summary>
        /// <param name="p1">First payload value.</param>
        /// <param name="p2">Second payload value.</param>
        public abstract void Execute(T1 p1, T2 p2);
    }

    /// <summary>
    /// Synchronous command with three payload values. Override <c>Execute(T1, T2, T3)</c> with your operation.
    /// </summary>
    /// <typeparam name="T1">Type of the first payload parameter.</typeparam>
    /// <typeparam name="T2">Type of the second payload parameter.</typeparam>
    /// <typeparam name="T3">Type of the third payload parameter.</typeparam>
    public abstract class Command<T1, T2, T3> : MvcSyncCommandBase
    {
        /// <summary>
        /// Implements the command's operation using the supplied payload.
        /// </summary>
        /// <param name="p1">First payload value.</param>
        /// <param name="p2">Second payload value.</param>
        /// <param name="p3">Third payload value.</param>
        public abstract void Execute(T1 p1, T2 p2, T3 p3);
    }

    /// <summary>
    /// Synchronous command with four payload values. Override <c>Execute(T1..T4)</c> with your operation.
    /// </summary>
    /// <typeparam name="T1">Type of the first payload parameter.</typeparam>
    /// <typeparam name="T2">Type of the second payload parameter.</typeparam>
    /// <typeparam name="T3">Type of the third payload parameter.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload parameter.</typeparam>
    public abstract class Command<T1, T2, T3, T4> : MvcSyncCommandBase
    {
        /// <summary>Implements the command's operation using the supplied payload.</summary>
        /// <param name="p1">First payload value.</param>
        /// <param name="p2">Second payload value.</param>
        /// <param name="p3">Third payload value.</param>
        /// <param name="p4">Fourth payload value.</param>
        public abstract void Execute(T1 p1, T2 p2, T3 p3, T4 p4);
    }

    /// <summary>
    /// Synchronous command with five payload values. Override <c>Execute(T1..T5)</c> with your operation.
    /// </summary>
    /// <typeparam name="T1">Type of the first payload parameter.</typeparam>
    /// <typeparam name="T2">Type of the second payload parameter.</typeparam>
    /// <typeparam name="T3">Type of the third payload parameter.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload parameter.</typeparam>
    /// <typeparam name="T5">Type of the fifth payload parameter.</typeparam>
    public abstract class Command<T1, T2, T3, T4, T5> : MvcSyncCommandBase
    {
        /// <summary>Implements the command's operation using the supplied payload.</summary>
        /// <param name="p1">First payload value.</param>
        /// <param name="p2">Second payload value.</param>
        /// <param name="p3">Third payload value.</param>
        /// <param name="p4">Fourth payload value.</param>
        /// <param name="p5">Fifth payload value.</param>
        public abstract void Execute(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5);
    }

    /// <summary>
    /// Synchronous command with six payload values. Override <c>Execute(T1..T6)</c> with your operation.
    /// </summary>
    /// <typeparam name="T1">Type of the first payload parameter.</typeparam>
    /// <typeparam name="T2">Type of the second payload parameter.</typeparam>
    /// <typeparam name="T3">Type of the third payload parameter.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload parameter.</typeparam>
    /// <typeparam name="T5">Type of the fifth payload parameter.</typeparam>
    /// <typeparam name="T6">Type of the sixth payload parameter.</typeparam>
    public abstract class Command<T1, T2, T3, T4, T5, T6> : MvcSyncCommandBase
    {
        /// <summary>Implements the command's operation using the supplied payload.</summary>
        /// <param name="p1">First payload value.</param>
        /// <param name="p2">Second payload value.</param>
        /// <param name="p3">Third payload value.</param>
        /// <param name="p4">Fourth payload value.</param>
        /// <param name="p5">Fifth payload value.</param>
        /// <param name="p6">Sixth payload value.</param>
        public abstract void Execute(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6);
    }

    /// <summary>
    /// Synchronous command with seven payload values. Override <c>Execute(T1..T7)</c> with your operation.
    /// </summary>
    /// <typeparam name="T1">Type of the first payload parameter.</typeparam>
    /// <typeparam name="T2">Type of the second payload parameter.</typeparam>
    /// <typeparam name="T3">Type of the third payload parameter.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload parameter.</typeparam>
    /// <typeparam name="T5">Type of the fifth payload parameter.</typeparam>
    /// <typeparam name="T6">Type of the sixth payload parameter.</typeparam>
    /// <typeparam name="T7">Type of the seventh payload parameter.</typeparam>
    public abstract class Command<T1, T2, T3, T4, T5, T6, T7> : MvcSyncCommandBase
    {
        /// <summary>Implements the command's operation using the supplied payload.</summary>
        /// <param name="p1">First payload value.</param>
        /// <param name="p2">Second payload value.</param>
        /// <param name="p3">Third payload value.</param>
        /// <param name="p4">Fourth payload value.</param>
        /// <param name="p5">Fifth payload value.</param>
        /// <param name="p6">Sixth payload value.</param>
        /// <param name="p7">Seventh payload value.</param>
        public abstract void Execute(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7);
    }

    /// <summary>
    /// Synchronous command with eight payload values. Override <c>Execute(T1..T8)</c> with your operation.
    /// </summary>
    /// <typeparam name="T1">Type of the first payload parameter.</typeparam>
    /// <typeparam name="T2">Type of the second payload parameter.</typeparam>
    /// <typeparam name="T3">Type of the third payload parameter.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload parameter.</typeparam>
    /// <typeparam name="T5">Type of the fifth payload parameter.</typeparam>
    /// <typeparam name="T6">Type of the sixth payload parameter.</typeparam>
    /// <typeparam name="T7">Type of the seventh payload parameter.</typeparam>
    /// <typeparam name="T8">Type of the eighth payload parameter.</typeparam>
    public abstract class Command<T1, T2, T3, T4, T5, T6, T7, T8> : MvcSyncCommandBase
    {
        /// <summary>Implements the command's operation using the supplied payload.</summary>
        /// <param name="p1">First payload value.</param>
        /// <param name="p2">Second payload value.</param>
        /// <param name="p3">Third payload value.</param>
        /// <param name="p4">Fourth payload value.</param>
        /// <param name="p5">Fifth payload value.</param>
        /// <param name="p6">Sixth payload value.</param>
        /// <param name="p7">Seventh payload value.</param>
        /// <param name="p8">Eighth payload value.</param>
        public abstract void Execute(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8);
    }

    /// <summary>
    /// Synchronous command with nine payload values. Override <c>Execute(T1..T9)</c> with your operation.
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
    public abstract class Command<T1, T2, T3, T4, T5, T6, T7, T8, T9> : MvcSyncCommandBase
    {
        /// <summary>Implements the command's operation using the supplied payload.</summary>
        /// <param name="p1">First payload value.</param>
        /// <param name="p2">Second payload value.</param>
        /// <param name="p3">Third payload value.</param>
        /// <param name="p4">Fourth payload value.</param>
        /// <param name="p5">Fifth payload value.</param>
        /// <param name="p6">Sixth payload value.</param>
        /// <param name="p7">Seventh payload value.</param>
        /// <param name="p8">Eighth payload value.</param>
        /// <param name="p9">Ninth payload value.</param>
        public abstract void Execute(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9);
    }

    /// <summary>
    /// Synchronous command with ten payload values. Override <c>Execute(T1..T10)</c> with your operation.
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
    public abstract class Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : MvcSyncCommandBase
    {
        /// <summary>Implements the command's operation using the supplied payload.</summary>
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
        public abstract void Execute(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10);
    }

    /// <summary>
    /// Synchronous command with eleven payload values. Override <c>Execute(T1..T11)</c> with your operation.
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
    public abstract class Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> : MvcSyncCommandBase
    {
        /// <summary>Implements the command's operation using the supplied payload.</summary>
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
        public abstract void Execute(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10, T11 p11);
    }

    /// <summary>
    /// Synchronous command with twelve payload values. Override <c>Execute(T1..T12)</c> with your operation.
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
    public abstract class Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> : MvcSyncCommandBase
    {
        /// <summary>Implements the command's operation using the supplied payload.</summary>
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
        public abstract void Execute(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10, T11 p11, T12 p12);
    }
}
