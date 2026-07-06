using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Logging;
using System;
using UnityEngine;

namespace mvcExpress.Internal.Initialization
{
    /// <summary>
    /// Shared registration helper for MonoBehaviour services that may expose different logic and view interfaces.
    /// </summary>
    internal static class ServiceRegistrationHelper
    {
        public static void RegisterServiceWithScopes(
            MvcDiContainer container,
            MonoBehaviour service,
            Type logicType,
            Type viewType,
            bool registerToLogic,
            bool registerToView,
            bool isTransient)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            if (service == null) throw new ArgumentNullException(nameof(service));

            var serviceType = service.GetType();

            try
            {
                DispatchRegistration(container, service, serviceType, logicType, viewType, registerToLogic, registerToView, isTransient);
            }
            catch (Exception ex)
            {
                var scope = registerToLogic && registerToView ? "logic+view" : registerToLogic ? "logic" : "view";
                var cause = ex.InnerException?.Message ?? ex.Message;
                MvcDebug.LogError($"Failed to register service '{service.name}' ({serviceType.Name}) to {scope}: {cause}\n(full: {ex})");
            }
        }

        private static void DispatchRegistration(
            MvcDiContainer container,
            MonoBehaviour service,
            Type serviceType,
            Type logicType,
            Type viewType,
            bool registerToLogic,
            bool registerToView,
            bool isTransient)
        {
            var method = typeof(ServiceRegistrationHelper).GetMethod(
                nameof(RegisterTyped),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            if (method == null)
            {
                throw new InvalidOperationException("Could not find RegisterTyped method.");
            }

            var generic = method.MakeGenericMethod(serviceType);
            generic.Invoke(null, new object[] { container, service, logicType, viewType, registerToLogic, registerToView, isTransient });
        }

        private static void RegisterTyped<TService>(
            MvcDiContainer container,
            MonoBehaviour service,
            Type logicType,
            Type viewType,
            bool registerToLogic,
            bool registerToView,
            bool isTransient) where TService : MonoBehaviour
        {
            var builder = container.Register((TService)service);

            if (registerToLogic)
            {
                if (logicType == typeof(TService))
                {
                    builder.ToLogic();
                }
                else
                {
                    CallToLogicAs<TService>(builder, logicType);
                }
            }

            if (registerToView)
            {
                if (viewType == typeof(TService))
                {
                    builder.ToView();
                }
                else
                {
                    CallToViewAs<TService>(builder, viewType);
                }
            }

            if (isTransient)
            {
                builder.AsTransient();
            }
            else
            {
                builder.AsPermanent();
            }
        }

        private static void CallToLogicAs<TService>(RegistrationBuilder<TService> builder, Type logicType) where TService : MonoBehaviour
        {
            var method = typeof(RegistrationBuilder<TService>).GetMethod(nameof(RegistrationBuilder<TService>.ToLogicAs));
            if (method == null)
            {
                throw new InvalidOperationException("Could not find ToLogicAs method.");
            }

            var generic = method.MakeGenericMethod(logicType);
            generic.Invoke(builder, null);
        }

        private static void CallToViewAs<TService>(RegistrationBuilder<TService> builder, Type viewType) where TService : MonoBehaviour
        {
            var method = typeof(RegistrationBuilder<TService>).GetMethod(nameof(RegistrationBuilder<TService>.ToViewAs));
            if (method == null)
            {
                throw new InvalidOperationException("Could not find ToViewAs method.");
            }

            var generic = method.MakeGenericMethod(viewType);
            generic.Invoke(builder, null);
        }
    }
}
