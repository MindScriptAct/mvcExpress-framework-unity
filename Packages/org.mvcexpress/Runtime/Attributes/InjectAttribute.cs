﻿using System;

namespace mvcExpress
{
    /// <summary>
    /// Marks a field or property for property injection from the owning module's DI container.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>[Inject]</b> is the framework's automated injection mechanism. The framework does not
    /// resolve constructor parameters - use <c>[Inject]</c> for all dependencies that must be
    /// supplied by the DI container.
    /// </para>
    /// <para>
    /// Injection scope depends on the actor type:<br/>
    /// - Commands, Proxies, and Services resolve from the <b>logic</b> scope.<br/>
    /// - Mediators resolve from the <b>view</b> scope.<br/>
    /// Injection runs before <c>OnInitialized</c> / command <c>Execute</c>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// public class PlayerCommand : Command
    /// {
    ///     [Inject] private PlayerProxy _playerProxy;
    ///
    ///     // Optional dep - won't throw if not registered
    ///     [Inject(optional: true)] private AnalyticsService _analytics;
    ///
    ///     public override void Execute() { }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class InjectAttribute : Attribute
    {
        /// <summary>
        /// When true the framework silently skips injection if the dependency is not registered,
        /// instead of throwing an exception.
        /// </summary>
        public bool Optional { get; }

        /// <summary>
        /// Creates an injection attribute.
        /// </summary>
        /// <param name="optional">
        /// Set to <c>true</c> to suppress missing-dependency exceptions. Default is <c>false</c>.
        /// </param>
        public InjectAttribute(bool optional = false)
        {
            Optional = optional;
        }
    }

    /// <summary>
    /// Marks a field or property for property injection from the <see cref="MvcFacade"/> global container.
    /// </summary>
    /// <remarks>
    /// Use this only for dependencies intentionally shared across all modules - services and
    /// proxies registered on <see cref="MvcFacade"/> rather than on a specific module. The global
    /// container is populated before any module initializes, so global deps are always available
    /// when module actors receive injection.
    /// </remarks>
    /// <example>
    /// <code>
    /// public class LeaderboardCommand : Command
    /// {
    ///     [InjectGlobal] private NetworkService _network; // registered on MvcFacade
    ///
    ///     public override void Execute() { }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class InjectGlobalAttribute : Attribute
    {
        /// <summary>
        /// When true the framework silently skips injection if the dependency is not registered
        /// in the global container, instead of throwing an exception.
        /// </summary>
        public bool Optional { get; }

        /// <summary>
        /// Creates a global injection attribute.
        /// </summary>
        /// <param name="optional">
        /// Set to <c>true</c> to suppress missing-dependency exceptions. Default is <c>false</c>.
        /// </param>
        public InjectGlobalAttribute(bool optional = false)
        {
            Optional = optional;
        }
    }
}
