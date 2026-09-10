namespace mvcExpress
{
    /// <summary>
    /// Defines the lifecycle of a registered dependency.
    /// </summary>
    public enum RegistrationLifecycle
    {
        /// <summary>
        /// Permanent dependencies live for the entire module (or application, for globals)
        /// lifetime. They cannot be unregistered or destroyed. Default and recommended for
        /// most use cases.
        /// </summary>
        Permanent = 0,

        /// <summary>
        /// Transient dependencies can be destroyed/unregistered dynamically.
        /// Use when the dependency has a dynamic lifecycle tied to runtime state
        /// (e.g., temporary game objects, session-based data).
        /// </summary>
        Transient = 1,

        /// <summary>
        /// Scoped dependencies get one dedicated instance per consuming actor (the "injectee"),
        /// created on that actor's first resolution and disposed exactly once, when the actor
        /// itself is torn down - not per resolution call or any other ambient window. Only
        /// supported for types the container can construct itself via
        /// <c>Activator.CreateInstance</c> - not MonoBehaviours.
        /// </summary>
        Scoped = 2,
    }
}
