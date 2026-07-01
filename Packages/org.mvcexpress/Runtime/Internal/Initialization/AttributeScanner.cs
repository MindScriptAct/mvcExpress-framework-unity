﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using mvcExpress.Logging;
using UnityEngine;

namespace mvcExpress.Internal.Initialization
{
    /// <summary>
    /// Scans all application assemblies once at startup and caches the [Register], [Bind], and
    /// [Attach] attribute metadata that <see cref="ModuleInitializer"/> consumes during each module's
    /// initialization.
    /// </summary>
    /// <remarks>
    /// Why this exists as a static, singleton cache:
    /// - Reflection is expensive. Scanning every assembly on every module creation would be
    ///   prohibitive in large projects. A one-time scan at <see cref="MvcFacade"/> startup amortises
    ///   the cost (~50-100 ms) across all subsequent module creations (&lt;1 ms lookup each).
    /// - Results are immutable after the scan, so concurrent reads from multiple modules are
    ///   safe without additional locking.
    ///
    /// Assembly filtering (biggest performance win):
    /// - Only assemblies that reference mvcExpress are scanned. Unity engine assemblies,
    ///   .NET runtime, and known third-party libraries are skipped immediately based on name
    ///   prefix. This typically eliminates ~90% of loaded assemblies.
    ///
    /// Cache layout (four separate dictionaries keyed by target module type):
    /// - A <c>UniversalModuleKey</c> sentinel stands in for registrations that have no specific
    ///   target module type (i.e. <c>[Register]</c> without <c>TargetModule</c>).
    ///   Lookups merge universal + module-specific lists at query time.
    /// - [Bind] always requires an explicit target module; the command cache therefore never
    ///   has a universal-key entry.
    ///
    /// Thread safety: double-checked locking around the scan; read-only after that.
    /// Internal - framework use only; not part of the public API.
    /// </remarks>
    internal static class AttributeScanner
    {
        // Sentinel type used as dictionary key for registrations without a target module type.
        // Using a real Type instead of null avoids Dictionary null-key issues and is self-documenting.
        private static readonly Type UniversalModuleKey = typeof(AttributeScanner);

        // Four separate caches - one per attribute family.
        // Key: target module Type (or UniversalModuleKey), Value: list of metadata structs.
        // Populated once during ScanAssemblies(); read-only afterwards.
        private static Dictionary<Type, List<ProxyRegistrationMetadata>> _proxyCache;
        private static Dictionary<Type, List<ServiceRegistrationMetadata>> _serviceCache;
        private static Dictionary<Type, List<CommandBindingMetadata>> _commandCache;
        private static Dictionary<Type, List<MediatorAttachmentMetadata>> _mediatorCache;

        // Global registration caches - no module grouping needed; all entries go into MvcFacade.
        private static List<GlobalRegistrationMetadata> _globalCache;
        private static List<StartupModuleMetadata> _startupModuleCache;

        // Guard flag and lock for double-checked initialization pattern.
        private static bool _isScanned = false;
        private static readonly object _scanLock = new object();

        /// <summary>
        /// True if assemblies have been scanned and metadata is cached.
        /// </summary>
        public static bool IsScanned => _isScanned;

        /// <summary>
        /// Scan all assemblies once and cache attribute metadata.
        /// Called automatically during MvcFacade.Initialize().
        /// Thread-safe and executes only once.
        /// </summary>
        internal static void ScanAssemblies()
        {
            if (_isScanned)
                return;

            lock (_scanLock)
            {
                if (_isScanned)
                    return;

                // Initialize caches
                _proxyCache = new Dictionary<Type, List<ProxyRegistrationMetadata>>(16);
                _serviceCache = new Dictionary<Type, List<ServiceRegistrationMetadata>>(16);
                _commandCache = new Dictionary<Type, List<CommandBindingMetadata>>(64);
                _mediatorCache = new Dictionary<Type, List<MediatorAttachmentMetadata>>(32);
                _globalCache = new List<GlobalRegistrationMetadata>(8);
                _startupModuleCache = new List<StartupModuleMetadata>(8);

                try
                {
                    PerformAssemblyScan();
                }
                catch (Exception ex)
                {
                    MvcDebug.LogError($"Attribute scan failed: {ex.Message}");
                }
                finally
                {
                    _isScanned = true;
                }
            }
        }

        /// <summary>
        /// Get cached proxy registration metadata for a specific module type.
        /// Returns empty list if no proxies are registered for the module.
        /// </summary>
        internal static IReadOnlyList<ProxyRegistrationMetadata> GetProxyMetadata(Type moduleType)
        {
            EnsureScanned();

            if (moduleType == null)
            {
                throw new ArgumentNullException(
                    nameof(moduleType),
                    "[AttributeScanner] Cannot get proxy metadata for null module type. " +
                    "This indicates a bug in ModuleInitializer - the module type was not provided during initialization. " +
                    "Check that ModuleInitializer receives a valid Type in its constructor.");
            }
            
            _proxyCache.TryGetValue(UniversalModuleKey, out var universal);
            _proxyCache.TryGetValue(moduleType, out var specific);

            if (universal == null) return (IReadOnlyList<ProxyRegistrationMetadata>)specific ?? Array.Empty<ProxyRegistrationMetadata>();
            if (specific == null) return universal;

            var combined = new List<ProxyRegistrationMetadata>(universal.Count + specific.Count);
            combined.AddRange(universal);
            combined.AddRange(specific);
            return combined;
        }

        /// <summary>
        /// Get cached service registration metadata for a specific module type.
        /// Returns empty list if no services are registered for the module.
        /// Merges universal (no target) and module-specific lists.
        /// </summary>
        internal static IReadOnlyList<ServiceRegistrationMetadata> GetServiceMetadata(Type moduleType)
        {
            EnsureScanned();

            if (moduleType == null)
            {
                throw new ArgumentNullException(
                    nameof(moduleType),
                    "[AttributeScanner] Cannot get service metadata for null module type. " +
                    "This indicates a bug in ModuleInitializer - the module type was not provided during initialization.");
            }
            
            _serviceCache.TryGetValue(UniversalModuleKey, out var universal);
            _serviceCache.TryGetValue(moduleType, out var specific);

            if (universal == null) return (IReadOnlyList<ServiceRegistrationMetadata>)specific ?? Array.Empty<ServiceRegistrationMetadata>();
            if (specific == null) return universal;

            var combined = new List<ServiceRegistrationMetadata>(universal.Count + specific.Count);
            combined.AddRange(universal);
            combined.AddRange(specific);

            return combined;
        }

        /// <summary>
        /// Get cached command binding metadata for a specific module type.
        /// Returns empty list if no commands are bound for the module.
        /// Command bindings always require an explicit target module, so there are no universal entries.
        /// </summary>
        internal static IReadOnlyList<CommandBindingMetadata> GetCommandMetadata(Type moduleType)
        {
            EnsureScanned();

            if (moduleType == null)
            {
                throw new ArgumentNullException(
                    nameof(moduleType),
                    "[AttributeScanner] Cannot get command metadata for null module type. " +
                    "This indicates a bug in ModuleInitializer - the module type was not provided during initialization.");
            }

            // [Bind] requires a target module type - universal (no-target) entries cannot exist.
            // BindAttribute constructor throws if targetModuleType is null, so _commandCache
            // never contains a UniversalModuleKey entry.
            _commandCache.TryGetValue(moduleType, out var specific);
            return (IReadOnlyList<CommandBindingMetadata>)specific ?? Array.Empty<CommandBindingMetadata>();
        }

        /// <summary>
        /// Get cached mediator attachment metadata for a specific module type.
        /// Returns empty list if no mediators are attached for the module.
        /// Merges universal (no target) and module-specific lists.
        /// </summary>
        internal static IReadOnlyList<MediatorAttachmentMetadata> GetMediatorMetadata(Type moduleType)
        {
            EnsureScanned();

            if (moduleType == null)
            {
                throw new ArgumentNullException(
                    nameof(moduleType),
                    "[AttributeScanner] Cannot get mediator metadata for null module type. " +
                    "This indicates a bug in ModuleInitializer - the module type was not provided during initialization.");
            }
            
            _mediatorCache.TryGetValue(UniversalModuleKey, out var universal);
            _mediatorCache.TryGetValue(moduleType, out var specific);

            if (universal == null) return (IReadOnlyList<MediatorAttachmentMetadata>)specific ?? Array.Empty<MediatorAttachmentMetadata>();
            if (specific == null) return universal;

            var combined = new List<MediatorAttachmentMetadata>(universal.Count + specific.Count);
            combined.AddRange(universal);
            combined.AddRange(specific);
            return combined;
        }

        /// <summary>
        /// Get cached global registration metadata collected from <c>[RegisterGlobal]</c> attributes.
        /// Returns an empty list if no global registrations were found.
        /// </summary>
        internal static IReadOnlyList<GlobalRegistrationMetadata> GetGlobalRegistrationMetadata()
        {
            EnsureScanned();
            return _globalCache ?? (IReadOnlyList<GlobalRegistrationMetadata>)Array.Empty<GlobalRegistrationMetadata>();
        }

        /// <summary>
        /// Get cached startup module metadata collected from <c>[StartupModule]</c> attributes.
        /// Returns an empty list if no startup modules were found.
        /// </summary>
        internal static IReadOnlyList<StartupModuleMetadata> GetStartupModuleMetadata()
        {
            EnsureScanned();
            return _startupModuleCache ?? (IReadOnlyList<StartupModuleMetadata>)Array.Empty<StartupModuleMetadata>();
        }

        /// <summary>
        /// Clear all cached metadata and reset scan state.
        /// Only for testing purposes.
        /// </summary>
        internal static void Reset()
        {
            lock (_scanLock)
            {
                _proxyCache?.Clear();
                _serviceCache?.Clear();
                _commandCache?.Clear();
                _mediatorCache?.Clear();
                _globalCache?.Clear();
                _startupModuleCache?.Clear();
                _isScanned = false;
            }
        }

        // Guards all Get* methods against being called before ScanAssemblies().
        // Throws clearly so callers get an actionable message rather than a NullReferenceException.
        private static void EnsureScanned()
        {
            if (!_isScanned)
            {
                throw new InvalidOperationException(
                    "[AttributeScanner] Metadata not available. Call ScanAssemblies() first during MvcFacade initialization.");
            }
        }

        // Entry point for the one-time reflection pass.
        // Iterates all loaded assemblies, skipping ones that cannot contain mvcExpress types,
        // then calls the three per-attribute scanners on each type.
        private static void PerformAssemblyScan()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            int scannedAssemblies = 0;
            int skippedAssemblies = 0;
            int totalTypes = 0;

            foreach (var assembly in assemblies)
            {
                // Assembly-level filtering (biggest performance win!)
                if (ShouldSkipAssembly(assembly))
                {
                    skippedAssemblies++;
                    continue;
                }

                scannedAssemblies++;

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // Some assemblies may fail to load all types - just use what we can get
                    types = ex.Types.Where(t => t != null).ToArray();
                }

                totalTypes += types.Length;

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract || type.IsInterface)
                        continue;

                    // Scan for [Register] attribute (Proxies and Services)
                    ScanForRegisterAttribute(type);

                    // Scan for [Bind] attribute (Commands)
                    ScanForBindAttribute(type);

                    // Scan for [Attach] and [AttachPrefab] attributes (Mediators)
                    ScanForAttachAttribute(type);

                    // Scan for [RegisterGlobal] attribute (global Proxies and Services)
                    ScanForRegisterGlobalAttribute(type);

                    // Scan for [StartupModule] attribute (auto-launch modules)
                    ScanForStartupModuleAttribute(type);

                }
            }

        }

        /// <summary>
        /// Determines if an assembly should be skipped during scanning.
        /// Skips Unity engine assemblies, .NET runtime, and assemblies that don't reference mvcExpress.
        /// This is the biggest performance optimization (~90% of assemblies skipped).
        /// </summary>
        private static bool ShouldSkipAssembly(Assembly assembly)
        {
            var assemblyName = assembly.GetName().Name;

            // Skip Unity engine assemblies (never contain user code)
            if (assemblyName.StartsWith("Unity", StringComparison.Ordinal))
                return true;

            // Skip .NET runtime assemblies
            if (assemblyName.StartsWith("System", StringComparison.Ordinal) ||
                assemblyName.StartsWith("mscorlib", StringComparison.Ordinal) ||
                assemblyName.StartsWith("netstandard", StringComparison.Ordinal) ||
                assemblyName.StartsWith("Microsoft.", StringComparison.Ordinal))
                return true;

            // Skip common third-party libraries that don't use mvcExpress
            if (assemblyName.StartsWith("Newtonsoft.", StringComparison.Ordinal) ||
                assemblyName.StartsWith("nunit.", StringComparison.Ordinal) ||
                assemblyName.StartsWith("NUnit.", StringComparison.Ordinal) ||
                assemblyName.StartsWith("Mono.", StringComparison.Ordinal))
                return true;

            // Always scan mvcExpress assemblies themselves
            if (assemblyName.StartsWith("org.mvcexpress", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.Equals("mvcExpress", StringComparison.OrdinalIgnoreCase))
                return false;

            // Check if assembly references mvcExpress (most important optimization)
            // If it doesn't reference mvcExpress, it can't contain types derived from
            // Proxy, Command, MediatorBehaviour, or use mvcExpress attributes
            try
            {
                var referencedAssemblies = assembly.GetReferencedAssemblies();
                foreach (var referencedAssembly in referencedAssemblies)
                {
                    var refName = referencedAssembly.Name;
                    if (refName.StartsWith("org.mvcexpress", StringComparison.OrdinalIgnoreCase) ||
                        refName.Equals("mvcExpress", StringComparison.OrdinalIgnoreCase))
                    {
                        return false; // Assembly references mvcExpress - scan it
                    }
                }

                // Assembly doesn't reference mvcExpress - skip it
                return true;
            }
            catch
            {
                // If we can't check references, err on the side of scanning
                // (better to scan too much than miss user code)
                return false;
            }
        }

        // Checks a type for [Register] attributes and files metadata into the correct cache
        // (proxy or service) based on the type hierarchy. MonoBehaviour services and plain C# services
        // both route to the service cache; Proxy/ProxyBehaviour go to the proxy cache.
        private static void ScanForRegisterAttribute(Type type)
        {
            var registerAttrs = type.GetCustomAttributes<RegisterAttribute>(inherit: false);
            if (!registerAttrs.Any())
                return;

            bool isProxy = typeof(mvcExpress.Proxy).IsAssignableFrom(type);
            bool isProxyBehaviour = typeof(mvcExpress.ProxyBehaviour).IsAssignableFrom(type);
            bool isMonoBehaviourService = typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(type) && !isProxyBehaviour;
            
            // Plain C# service: any class with [Register] that's not a Proxy/ProxyBehaviour/MonoBehaviour
            bool isPlainService = !isProxy && !isProxyBehaviour && !isMonoBehaviourService;

            // Accept: Proxy, ProxyBehaviour, MonoBehaviour services, OR plain C# services
            if (!isProxy && !isProxyBehaviour && !isMonoBehaviourService && !isPlainService)
            {
                MvcDebug.LogWarning(
                    $"Type '{type.FullName}' has [Register] attribute " +
                    $"but could not be classified. This should never happen - please report this bug.");
                return;
            }

            foreach (var attr in registerAttrs)
            {
                try
                {
                    // Use UniversalModuleKey instead of null for universal/module-agnostic registrations
                    var targetType = attr.TargetModuleType ?? UniversalModuleKey;

                    // MonoBehaviour services OR plain C# services both go to service cache
                    if (isMonoBehaviourService || isPlainService)
                    {
                        if (!_serviceCache.TryGetValue(targetType, out var list))
                            _serviceCache[targetType] = list = new List<ServiceRegistrationMetadata>();

                        list.Add(new ServiceRegistrationMetadata(type, attr));
                    }
                    else // Proxy or ProxyBehaviour
                    {
                        if (!_proxyCache.TryGetValue(targetType, out var list))
                            _proxyCache[targetType] = list = new List<ProxyRegistrationMetadata>();

                        list.Add(new ProxyRegistrationMetadata(type, attr));
                    }
                }
                catch (Exception ex)
                {
                    MvcDebug.LogError($"Failed to cache [Register] metadata for '{type.FullName}': {ex.Message}");
                }
            }
        }

        // Checks a type for [Bind] attributes and adds CommandBindingMetadata to the command cache.
        // Early-exits immediately if the type is not a command - avoids attribute reflection on
        // the vast majority of user types.
        private static void ScanForBindAttribute(Type type)
        {
            // Only scan types that could be commands
            if (!typeof(mvcExpress.MvcCommandBase).IsAssignableFrom(type))
                return;

            var bindAttrs = type.GetCustomAttributes<BindAttribute>(inherit: false);
            if (!bindAttrs.Any())
                return;

            foreach (var attr in bindAttrs)
            {
                try
                {
                    // Use UniversalModuleKey instead of null
                    var targetType = attr.TargetModuleType ?? UniversalModuleKey;

                    if (!_commandCache.TryGetValue(targetType, out var list))
                        _commandCache[targetType] = list = new List<CommandBindingMetadata>();

                    list.Add(new CommandBindingMetadata(type, attr));
                }
                catch (Exception ex)
                {
                    MvcDebug.LogError($"Failed to cache [Bind] metadata for '{type.FullName}': {ex.Message}");
                }
            }
        }

        // Checks a type for [Attach] and [AttachPrefab] attributes and adds MediatorAttachmentMetadata
        // to the mediator cache. Early-exits if the type is not a MediatorBehaviour.
        private static void ScanForAttachAttribute(Type type)
        {
            // Only scan types that could be mediators
            if (!typeof(mvcExpress.MediatorBehaviour).IsAssignableFrom(type))
                return;

            var attachAttrs = type.GetCustomAttributes<AttachAttribute>(inherit: false);
            var attachPrefabAttrs = type.GetCustomAttributes<AttachPrefabAttribute>(inherit: false);
            if (!attachAttrs.Any() && !attachPrefabAttrs.Any())
                return;

            foreach (var attr in attachAttrs)
            {
                try
                {
                    // Use UniversalModuleKey instead of null
                    var targetType = attr.TargetModuleType ?? UniversalModuleKey;

                    if (!_mediatorCache.TryGetValue(targetType, out var list))
                        _mediatorCache[targetType] = list = new List<MediatorAttachmentMetadata>();

                    list.Add(new MediatorAttachmentMetadata(type, attr));
                }
                catch (Exception ex)
                {
                    MvcDebug.LogError($"Failed to cache [Attach] metadata for '{type.FullName}': {ex.Message}");
                }
            }

            foreach (var attr in attachPrefabAttrs)
            {
                try
                {
                    var targetType = attr.TargetModuleType ?? UniversalModuleKey;

                    if (!_mediatorCache.TryGetValue(targetType, out var list))
                        _mediatorCache[targetType] = list = new List<MediatorAttachmentMetadata>();

                    list.Add(new MediatorAttachmentMetadata(type, attr));
                }
                catch (Exception ex)
                {
                    MvcDebug.LogError($"Failed to cache [AttachPrefab] metadata for '{type.FullName}': {ex.Message}");
                }
            }
        }

        // Checks a type for a [RegisterGlobal] attribute and adds GlobalRegistrationMetadata to
        // the global cache. MonoBehaviour types are rejected because they cannot be created via
        // Activator.CreateInstance - a GameObject is required, which is a Unity concern outside
        // the scope of assembly-time attribute registration.
        private static void ScanForRegisterGlobalAttribute(Type type)
        {
            var attr = type.GetCustomAttribute<RegisterGlobalAttribute>(inherit: false);
            if (attr == null)
                return;

            // MonoBehaviour types cannot be instantiated via Activator.CreateInstance.
            if (typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(type))
            {
                MvcDebug.LogError(
                    $"[RegisterGlobal] Type '{type.FullName}' is a MonoBehaviour. " +
                    $"MonoBehaviour types cannot be registered globally via attribute because they require " +
                    $"a Unity GameObject. Use the GlobalServiceRegistryBehaviour or GlobalProxyRegistryBehaviour " +
                    $"Inspector components instead.");
                return;
            }

            // Accept: plain C# Proxy or any non-MonoBehaviour class (service).
            try
            {
                _globalCache.Add(new GlobalRegistrationMetadata(type, attr));
            }
            catch (Exception ex)
            {
                MvcDebug.LogError($"Failed to cache [RegisterGlobal] metadata for '{type.FullName}': {ex.Message}");
            }
        }

        // Checks a type for a [StartupModule] attribute and adds StartupModuleMetadata to the
        // startup module cache. Early-exits if the type does not inherit MvcModule.
        private static void ScanForStartupModuleAttribute(Type type)
        {
            // Only module subclasses are valid targets - fast path for all other types.
            if (!typeof(MvcModule).IsAssignableFrom(type))
                return;

            var attr = type.GetCustomAttribute<StartupModuleAttribute>(inherit: false);
            if (attr == null)
                return;

            try
            {
                _startupModuleCache.Add(new StartupModuleMetadata(type, attr));
            }
            catch (Exception ex)
            {
                MvcDebug.LogError($"Failed to cache [StartupModule] metadata for '{type.FullName}': {ex.Message}");
            }
        }
    }
}
