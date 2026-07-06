namespace mvcExpress
{
    /// <summary>
    /// Lifecycle callbacks for Services that participate in module initialization and teardown.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The framework registers all services first (Unity registry, then <c>[Register]</c>
    /// attribute, then code), and only after all three sources are complete does it inject
    /// dependencies and call <see cref="OnInitialized"/>. This two-phase split means every
    /// service - including other services registered after this one - is already in the DI
    /// container by the time <see cref="OnInitialized"/> runs, so cross-service injection
    /// and circular <c>[Inject]</c> dependencies both resolve correctly.
    /// </para>
    /// <para>
    /// <see cref="OnCleanup"/> is called in reverse registration order during module destruction,
    /// before the DI container is torn down. Use it to release subscriptions, timers, or any
    /// resources acquired in <see cref="OnInitialized"/>.
    /// </para>
    /// <para>
    /// Works for plain classes and MonoBehaviour services alike. <c>Proxy</c> and
    /// <c>ProxyBehaviour</c> already expose their own <c>OnInitialized</c> and <c>OnCleanup</c>
    /// overrides and do not need this interface. <c>MediatorBehaviour</c> handles both hooks
    /// separately as well.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [Register]
    /// public class ScoreService : IMvcLifecycle
    /// {
    ///     [Inject] private ScoreProxy _proxy;
    ///
    ///     public void OnInitialized()
    ///     {
    ///         // All services are in the container at this point.
    ///         // [Inject] fields are guaranteed to be filled before this call.
    ///         _proxy.Reset();
    ///     }
    ///
    ///     public void OnCleanup()
    ///     {
    ///         // Called in reverse registration order when the module is destroyed.
    ///     }
    /// }
    /// </code>
    /// </example>
    public interface IMvcLifecycle
    {
        /// <summary>
        /// Called by the framework after the registered instance has received all injected dependencies.
        /// </summary>
        void OnInitialized();

        /// <summary>
        /// Called by the framework before the module's DI container is torn down.
        /// Invoked in reverse registration order (last registered, first cleaned up).
        /// </summary>
        void OnCleanup();
    }
}
