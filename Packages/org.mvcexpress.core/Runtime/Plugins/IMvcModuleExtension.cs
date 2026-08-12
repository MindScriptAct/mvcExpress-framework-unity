namespace mvcExpress.Plugins
{
    /// <summary>
    /// Runtime extension hook for <see cref="MvcModule"/>. Components implementing this
    /// interface, placed on the module GameObject or any of its children, are discovered
    /// once during module initialization and called at defined lifecycle points.
    /// </summary>
    /// <remarks>
    /// Extensions must not depend on ordering between each other within the same hook
    /// point. State created in any <see cref="OnExtensionSetup"/> is visible to every
    /// <see cref="OnModuleInitialized"/>. A throwing extension fails module
    /// initialization loudly, consistent with core phase failures.
    /// </remarks>
    public interface IMvcModuleExtension
    {
        /// <summary>
        /// Called before the Services phase. Create extension state here and register
        /// shared instances into <see cref="MvcModuleExtensionContext.Container"/> so
        /// event-domain actors can inject them.
        /// </summary>
        void OnExtensionSetup(MvcModuleExtensionContext context);

        /// <summary>
        /// Called after the module completed initialization (after the module's own
        /// OnInitialized hook). Messenger and DI are fully ready.
        /// </summary>
        void OnModuleInitialized(MvcModuleExtensionContext context);

        /// <summary>
        /// Called during module teardown (OnDestroy), before core containers clear.
        /// </summary>
        void OnModuleDestroy();
    }
}
