using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Logging;
using System;
using UnityEngine;

namespace mvcExpress.Internal.Initialization
{
    /// <summary>
    /// Bridges the gap between runtime <see cref="Type"/> objects and the compile-time generic
    /// <c>Register&lt;T&gt;</c> builder on <see cref="MvcDiContainer"/>, using minimal reflection
    /// only once per call to dispatch into strongly-typed methods.
    /// </summary>
    /// <remarks>
    /// Why this exists:
    /// The DI container's fluent builder API is generic for type safety and performance.
    /// At runtime, proxy types are known only as <see cref="Type"/> references (from
    /// Inspector mappings, attributes, etc.). This helper encapsulates the single reflection
    /// call needed to make the generic method and then delegates everything else to type-safe
    /// generic builder calls - no further reflection occurs inside <see cref="RegisterTyped{TProxy}"/>.
    ///
    /// Fast paths:
    /// - If logicType == concreteType, calls <c>ToLogic()</c> (no generic dispatch needed).
    /// - If viewType  == concreteType, calls <c>ToView()</c> (no generic dispatch needed).
    /// - Otherwise, one reflection call per scope to invoke <c>ToLogicAs&lt;T&gt;()</c> /
    ///   <c>ToViewAs&lt;T&gt;()</c>.
    ///
    /// Only handles <see cref="ProxyBehaviour"/> types. Plain C# <see cref="Proxy"/> registration
    /// is done inline in <see cref="ModuleInitializer"/> via the non-generic <c>Register(object, Type)</c>
    /// overload (no helper needed there).
    ///
    /// Internal - not part of the public API.
    /// </remarks>
    internal static class ProxyRegistrationHelper
    {
        /// <summary>
        /// Legacy entry point that converts the old <c>nonView</c> flag into the
        /// <c>registerToLogic/registerToView</c> pair and delegates to
        /// <see cref="RegisterProxyWithScopes"/>.
        /// </summary>
        /// <param name="container">The DI container to register the proxy into.</param>
        /// <param name="proxy">The proxy behaviour instance to register.</param>
        /// <param name="logicType">Type used to look up the proxy from logic scope.</param>
        /// <param name="viewType">Type used to look up the proxy from view scope.</param>
        /// <param name="nonView">When <c>true</c>, the proxy is only registered to the logic scope
        /// and mediators cannot inject it.</param>
        /// <param name="isTransient">When <c>true</c>, the registration is scoped to module lifetime.</param>
        public static void RegisterProxy(
            MvcDiContainer container,
            ProxyBehaviour proxy,
            Type logicType,
            Type viewType,
            bool nonView,
            bool isTransient = false)
        {
            // Convert old NonView flag to new scope flags
            bool registerToLogic = true;  // Logic is always enabled in old system
            bool registerToView = !nonView;  // View enabled if NonView is false
            
            RegisterProxyWithScopes(container, proxy, logicType, viewType, registerToLogic, registerToView, isTransient);
        }

        /// <summary>
        /// Register a proxy with explicit logic and view scope control.
        /// </summary>
        public static void RegisterProxyWithScopes(
            MvcDiContainer container,
            ProxyBehaviour proxy,
            Type logicType,
            Type viewType,
            bool registerToLogic,
            bool registerToView,
            bool isTransient = false)
        {
            var proxyType = proxy.GetType();

            try
            {
                // Dispatch to generic method - one-time reflection for type dispatch
                DispatchRegistration(container, proxy, proxyType, logicType, viewType, registerToLogic, registerToView, isTransient);
            }
            catch (Exception ex)
            {
                var scope = registerToLogic && registerToView ? "logic+view" : registerToLogic ? "logic" : "view";
                var cause = ex.InnerException?.Message ?? ex.Message;
                MvcDebug.LogError($"Failed to register proxy '{proxy.name}' to {scope}: {cause}\n(full: {ex})");
            }
        }

        // Kept private - callers should go through RegisterProxyWithScopes.
        // Factored out to keep RegisterProxyWithScopes readable; the logic is identical
        // to the non-view branch of RegisterProxyWithScopes.
        private static void RegisterNonViewProxy(
            MvcDiContainer container,
            ProxyBehaviour proxy,
            Type proxyType,
            Type logicType,
            bool isTransient)
        {
            try
            {
                // Dispatch to generic method - one-time reflection for type dispatch
                DispatchRegistration(container, proxy, proxyType, logicType, null, registerToLogic: true, registerToView: false, isTransient);
            }
            catch (Exception ex)
            {
                MvcDebug.LogError($"Failed to register non-view proxy '{proxy.name}': {ex.Message}");
            }
        }

        // Kept private - callers should go through RegisterProxyWithScopes.
        private static void RegisterViewAccessibleProxy(
            MvcDiContainer container,
            ProxyBehaviour proxy,
            Type proxyType,
            Type logicType,
            Type viewType,
            bool isTransient)
        {
            if (viewType == null)
            {
                MvcDebug.LogError($"View type is null for proxy '{proxy.name}'. Cannot register for view access.");
                return;
            }

            try
            {
                // Dispatch to generic method - one-time reflection for type dispatch
                DispatchRegistration(container, proxy, proxyType, logicType, viewType, registerToLogic: true, registerToView: true, isTransient);
            }
            catch (Exception ex)
            {
                MvcDebug.LogError($"Failed to register view-accessible proxy '{proxy.name}': {ex.Message}");
            }
        }

        /// <summary>
        /// Dispatch to the strongly-typed generic registration method.
        /// Uses minimal reflection only to bridge from runtime Type to compile-time generic parameter.
        /// </summary>
        private static void DispatchRegistration(
            MvcDiContainer container,
            ProxyBehaviour proxy,
            Type proxyType,
            Type logicType,
            Type viewType,
            bool registerToLogic,
            bool registerToView,
            bool isTransient)
        {
            // Determine which generic method to call based on type relationships
            var method = typeof(ProxyRegistrationHelper).GetMethod(
                nameof(RegisterTyped),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            if (method == null)
            {
                throw new InvalidOperationException("Could not find RegisterTyped method.");
            }

            // Make generic with the proxy type - this is the only generic parameter we need
            var genericMethod = method.MakeGenericMethod(proxyType);
            
            // Invoke - after this point, everything is strongly-typed
            genericMethod.Invoke(null, new object[] { container, proxy, logicType, viewType, registerToLogic, registerToView, isTransient });
        }

        /// <summary>
        /// Strongly-typed registration method.
        /// Called once via reflection, but all subsequent operations are compile-time type-safe.
        /// No reflection used within this method - pure generic builder pattern.
        /// </summary>
        private static void RegisterTyped<TProxy>(
            MvcDiContainer container,
            TProxy proxy,
            Type logicType,
            Type viewType,
            bool registerToLogic,
            bool registerToView,
            bool isTransient) where TProxy : ProxyBehaviour
        {
            // Start with the strongly-typed builder
            var builder = container.Register(proxy);

            // Configure logic registration (type-safe)
            if (registerToLogic)
            {
                if (logicType == typeof(TProxy))
                {
                    // Fast path: concrete type
                    builder.ToLogic();
                }
                else
                {
                    // Typed path: interface or base class
                    CallToLogicAs<TProxy>(builder, logicType);
                }
            }

            // Configure view registration (type-safe)
            if (registerToView)
            {
                if (viewType == typeof(TProxy))
                {
                    // Fast path: concrete type
                    builder.ToView();
                }
                else
                {
                    // Typed path: interface or base class
                    CallToViewAs<TProxy>(builder, viewType);
                }
            }

            // Complete registration with appropriate lifecycle
            if (isTransient)
            {
                builder.AsTransient();
            }
            else
            {
                builder.AsPermanent();
            }
        }

        /// <summary>
        /// Call ToLogicAs{TLogic} on the builder with the specified logic type.
        /// Uses one-time reflection to invoke the generic method with the correct type parameter.
        /// </summary>
        private static void CallToLogicAs<TProxy>(RegistrationBuilder<TProxy> builder, Type logicType)
        {
            var method = typeof(RegistrationBuilder<TProxy>).GetMethod(nameof(RegistrationBuilder<TProxy>.ToLogicAs));
            if (method == null)
            {
                throw new InvalidOperationException("Could not find ToLogicAs method.");
            }

            var genericMethod = method.MakeGenericMethod(logicType);
            genericMethod.Invoke(builder, null);
        }

        /// <summary>
        /// Call ToViewAs{TView} on the builder with the specified view type.
        /// Uses one-time reflection to invoke the generic method with the correct type parameter.
        /// </summary>
        private static void CallToViewAs<TProxy>(RegistrationBuilder<TProxy> builder, Type viewType)
        {
            var method = typeof(RegistrationBuilder<TProxy>).GetMethod(nameof(RegistrationBuilder<TProxy>.ToViewAs));
            if (method == null)
            {
                throw new InvalidOperationException("Could not find ToViewAs method.");
            }

            var genericMethod = method.MakeGenericMethod(viewType);
            genericMethod.Invoke(builder, null);
        }
    }
}
