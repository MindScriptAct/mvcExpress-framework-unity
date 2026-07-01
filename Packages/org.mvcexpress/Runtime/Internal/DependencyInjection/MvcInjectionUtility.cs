using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using mvcExpress;
using mvcExpress.Internal.Commands;
using mvcExpress.Logging;

namespace mvcExpress.Internal.DependencyInjection
{
    /// <summary>
    /// Reflection helper responsible for wiring members marked with <see cref="InjectAttribute"/> and <see cref="InjectGlobalAttribute"/>.
    /// AUTOMATIC TRANSIENT TRACKING: Notifies command processor when transient dependencies are resolved.
    /// </summary>
    internal static class MvcInjectionUtility
    {
        private enum MemberKind
        {
            Field,
            Property
        }

        /// <summary>
        /// Cached description of one injectable field or property.
        /// </summary>
        private readonly struct InjectMember
        {
            public InjectMember(MemberKind kind, MemberInfo member, Type memberType, bool optional, bool global)
            {
                Kind = kind;
                Member = member;
                MemberType = memberType;
                Optional = optional;
                Global = global;
            }

            public MemberKind Kind { get; }
            public MemberInfo Member { get; }
            public Type MemberType { get; }
            public bool Optional { get; }
            public bool Global { get; }
        }

        private static readonly ConcurrentDictionary<Type, InjectMember[]> _memberCache = new ConcurrentDictionary<Type, InjectMember[]>();

        /// <summary>
        /// Injects members marked with [Inject] and [InjectGlobal] attributes.
        /// AUTOMATIC TRANSIENT TRACKING: For commands, notifies processor about transient dependencies.
        /// </summary>
        internal static void InjectMembers(object target, MvcDiContainer container, bool useViewScope, MvcCommandProcessor commandProcessor = null)
        {
            if (target == null || container == null)
            {
                return;
            }

            var type = target.GetType();
            var members = _memberCache.GetOrAdd(type, BuildMemberDescriptors); // Cache reflection results per type

            if (members.Length == 0)
            {
                return; // Nothing to inject
            }

            // Check if this is a command (need to track transient dependencies)
            bool isCommand = commandProcessor != null && target is MvcCommandBase;
            bool isAsync = isCommand && target is CommandAsync;

            for (int i = 0; i < members.Length; i++)
            {
                // Resolve and assign each discovered member
                Assign(target, members[i], container, useViewScope, commandProcessor, isCommand, isAsync);
            }
        }

        private static InjectMember[] BuildMemberDescriptors(Type type)
        {
            var list = new List<InjectMember>();
            while (type != null && type != typeof(object))
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

                var fields = type.GetFields(flags);
                for (int i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    if (field.IsStatic) continue;

                    var globalAttr = field.GetCustomAttribute<InjectGlobalAttribute>(inherit: true);
                    if (globalAttr != null)
                    {
                        list.Add(new InjectMember(MemberKind.Field, field, field.FieldType, globalAttr.Optional, global: true));
                        continue;
                    }

                    var attr = field.GetCustomAttribute<InjectAttribute>(inherit: true);
                    if (attr != null)
                    {
                        list.Add(new InjectMember(MemberKind.Field, field, field.FieldType, attr.Optional, global: false));
                    }
                }

                var properties = type.GetProperties(flags);
                for (int i = 0; i < properties.Length; i++)
                {
                    var prop = properties[i];
                    if (prop.GetMethod?.IsStatic ?? true) continue;
                    if (!prop.CanWrite) continue;

                    var globalAttr = prop.GetCustomAttribute<InjectGlobalAttribute>(inherit: true);
                    if (globalAttr != null)
                    {
                        list.Add(new InjectMember(MemberKind.Property, prop, prop.PropertyType, globalAttr.Optional, global: true));
                        continue;
                    }

                    var attr = prop.GetCustomAttribute<InjectAttribute>(inherit: true);
                    if (attr != null)
                    {
                        list.Add(new InjectMember(MemberKind.Property, prop, prop.PropertyType, attr.Optional, global: false));
                    }
                }

                type = type.BaseType;
            }

            return list.ToArray();
        }

        private static void Assign(object target, InjectMember descriptor, MvcDiContainer container, bool useViewScope,
            MvcCommandProcessor commandProcessor, bool isCommand, bool isAsync)
        {
            var targetContainer = descriptor.Global ? MvcFacade.Global : container;

            if (!targetContainer.TryResolveInternal(descriptor.MemberType, useViewScope, out var resolved))
            {
                if (descriptor.Optional)
                {
                    return;
                }

                var containerName = descriptor.Global ? "global" : (useViewScope ? "view" : "logic");
                var registeredTypes = targetContainer.GetRegisteredTypes();
                var registeredList = registeredTypes.Count > 0
                    ? string.Join(", ", registeredTypes)
                    : "<none>";

                var errorMessage = $"[MvcExpress] Dependency injection failed.\n\n" +
                    $"Cannot inject '{descriptor.MemberType.Name}' into '{target.GetType().Name}'.\n\n" +
                    $"Target Container: {containerName}\n" +
                    $"Requested Type: {descriptor.MemberType.FullName}\n" +
                    $"Registered Types in {containerName} container: {registeredList}\n\n" +
                    $"SOLUTION:\n" +
                    $"1. Ensure '{descriptor.MemberType.Name}' is registered in RegisterProxies() or RegisterServices()\n" +
                    $"2. Use ToLogic() for proxies/services: Register(new {descriptor.MemberType.Name}()).ToLogic().AsPersistent()\n" +
                    $"3. Check if the type name matches exactly (case-sensitive)\n" +
                    $"4. For view-scoped types, use ToView() instead of ToLogic()";

                throw new InvalidOperationException(errorMessage);
            }

            if (isCommand && !descriptor.Global)
            {
                if (targetContainer.IsTransient(descriptor.MemberType))
                {
                    commandProcessor.OnDependencyResolved(target.GetType(), descriptor.MemberType, isAsync);
                }

                if (targetContainer.IsScoped(descriptor.MemberType))
                {
                    commandProcessor.OnScopedDependencyResolved();
                }
            }

            switch (descriptor.Kind)
            {
                case MemberKind.Field:
                    ((FieldInfo)descriptor.Member).SetValue(target, resolved);
                    break;
                case MemberKind.Property:
                    ((PropertyInfo)descriptor.Member).SetValue(target, resolved);
                    break;
            }
        }

        /// <summary>
        /// Pre-warms the reflection cache for known types to avoid reflection during gameplay.
        /// Call during module initialization with your service/proxy/mediator types.
        /// </summary>
        /// <param name="types">Types to pre-cache (services, proxies, commands, mediators)</param>
        internal static void PreWarmCache(params Type[] types)
        {
            if (types == null || types.Length == 0) return;

            for (int i = 0; i < types.Length; i++)
            {
                var type = types[i];
                if (type != null && !_memberCache.ContainsKey(type))
                {
                    _memberCache.TryAdd(type, BuildMemberDescriptors(type));
                }
            }

        }

        /// <summary>
        /// Pre-warms cache for all types in the provided assemblies that might need injection.
        /// Useful for warming cache for all framework types at startup.
        /// </summary>
        internal static void PreWarmCacheFromAssemblies(params Assembly[] assemblies)
        {
            if (assemblies == null || assemblies.Length == 0) return;

            var typesToCache = new List<Type>();

            for (int i = 0; i < assemblies.Length; i++)
            {
                var asm = assemblies[i];
                if (asm == null) continue;

                try
                {
                    var types = asm.GetTypes();
                    for (int j = 0; j < types.Length; j++)
                    {
                        var type = types[j];
                        if (type == null || type.IsAbstract || type.IsInterface) continue;

                        // Cache types that likely need injection
                        if (typeof(MvcCommandBase).IsAssignableFrom(type) ||
                            typeof(mvcExpress.Proxy).IsAssignableFrom(type) ||
                            typeof(mvcExpress.ProxyBehaviour).IsAssignableFrom(type) ||
                            typeof(mvcExpress.MediatorBehaviour).IsAssignableFrom(type))
                        {
                            typesToCache.Add(type);
                        }
                    }
                }
                catch (ReflectionTypeLoadException ex)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    MvcDebug.LogWarning($"Skipped assembly '{asm.FullName}' while pre-warming injection cache: {ex.Message}");
#endif
                }
            }

            PreWarmCache(typesToCache.ToArray());
        }
    }
}
