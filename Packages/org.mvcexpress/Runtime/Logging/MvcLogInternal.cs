using System.Diagnostics;
using System.Runtime.CompilerServices;
using System;
using UnityEngine;

namespace mvcExpress.Logging
{
    /// <summary>
    /// Central internal logging hub that routes framework-generated log events to the active
    /// <see cref="IMvcLogger"/> and all registered <see cref="IMvcLoggerPlugin"/> instances.
    /// </summary>
    /// <remarks>
    /// This class is internal - user code should call <see cref="MvcDebug"/> instead.
    ///
    /// Gating:
    /// Every log method calls <c>ShouldLog(module)</c> which returns <c>true</c> when either
    /// the global <see cref="LoggingEnabled"/> flag is set, or the specific module has its own
    /// <c>LoggingEnabled</c> flag set. This lets teams enable verbose logging for a single module
    /// in the Unity Inspector without flooding the console with events from unrelated modules.
    ///
    /// Compilation:
    /// All public static methods are decorated with <c>[Conditional("UNITY_EDITOR")]</c> and
    /// <c>[Conditional("MVC_LOGGING")]</c> so call sites compile away completely in production
    /// unless the <c>MVC_LOGGING</c> scripting define symbol is active. Clean release builds
    /// contain zero logging code - only <c>throw</c> statements remain.
    ///
    /// Structured methods:
    /// The class provides one method per meaningful framework event (proxy registered, command bound,
    /// message published, etc.) rather than a generic string log. This gives log consumers typed
    /// context objects (<see cref="MvcLogContext"/>) for building rich editor tooling.
    ///
    /// Plugin extensibility:
    /// Third-party tools (e.g. MvcConsole Pro) register via <see cref="RegisterPlugin"/> and receive
    /// every log event that passes the gating check, alongside the primary logger.
    /// </remarks>
    internal static class MvcLogInternal
    {
        // The active primary logger; defaults to the Unity console fallback.
        // Replaced via SetLogger() during app startup for custom integrations.
        private static IMvcLogger _logger = new UnityConsoleLogger();
        // Global enable/disable flag. When false, per-module flags still gate individual modules.
        private static bool _loggingEnabled = false;
        // When false, UnityConsoleLogger becomes a no-op and only plugins receive the log event.
        private static bool _useUnityDebugFallback = true;
        // Stack-trace walking for accurate caller info; disabled by default due to performance cost.
        private static bool _enableDetailedCallerInfo = false;
        /// <summary>
        /// Register a plugin to receive log events.
        /// </summary>
        [Obsolete("Use MvcPluginBus.Register(observer) instead. IMvcLoggerPlugin is replaced by mvcExpress.Plugins.ILogObserver.")]
        public static void RegisterPlugin(IMvcLoggerPlugin plugin)
        {
        }

        /// <summary>
        /// Unregister a plugin.
        /// </summary>
        [Obsolete("Use MvcPluginBus.Unregister(observer) instead. IMvcLoggerPlugin is replaced by mvcExpress.Plugins.ILogObserver.")]
        public static void UnregisterPlugin(IMvcLoggerPlugin plugin)
        {
        }

        /// <summary>
        /// Set custom logger implementation.
        /// </summary>
        public static void SetLogger(IMvcLogger logger)
        {
            _logger = logger ?? new UnityConsoleLogger();
        }

        /// <summary>
        /// Enable or disable logging for the entire framework.
        /// This setting is synchronized with Project Settings in the editor.
        /// </summary>
        public static bool LoggingEnabled
        {
            get => _loggingEnabled;
            set => _loggingEnabled = value;
        }

        /// <summary>
        /// Enable or disable Unity Debug.Log fallback when no custom logger is set.
        /// When disabled, logs are only emitted through the configured logger and plugins.
        /// This setting is synchronized with Project Settings in the editor.
        /// </summary>
        public static bool UseUnityDebugFallback
        {
            get => _useUnityDebugFallback;
            set => _useUnityDebugFallback = value;
        }

        /// <summary>
        /// Enable or disable detailed caller information via stack trace walking.
        /// When enabled, logs will include accurate file/line info by walking the stack trace,
        /// but this has performance cost (allocations + CPU). 
        /// When disabled (default), uses [CallerFilePath] attributes which are faster but less accurate.
        /// 
        /// Performance impact:
        /// - Enabled: ~0.1-0.5ms per message publish (can add up in message-heavy scenarios)
        /// - Disabled: Near-zero overhead (compiler injects caller info at compile time)
        /// 
        /// Recommendation: Keep disabled in production; enable only for debugging specific issues.
        /// </summary>
        public static bool EnableDetailedCallerInfo
        {
            get => _enableDetailedCallerInfo;
            set => _enableDetailedCallerInfo = value;
        }
        /// <summary>
        /// Log message publication from mediator/proxy.
        /// </summary>
        [Conditional("UNITY_EDITOR"), Conditional("MVC_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogMessagePublished<TMessage>(
            object publisher,
            mvcExpress.MvcModule module,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            if (!ShouldLog(module)) return;

            var messageTypeName = typeof(TMessage).Name;
            var sourceTypeName = publisher?.GetType().Name ?? "Unknown";

            // Only perform expensive stack trace walking if explicitly enabled
            // Default: use fast [CallerFilePath] attributes for near-zero overhead
            if (_enableDetailedCallerInfo)
            {
                var actualCaller = FindActualCaller();
                if (actualCaller.HasValue)
                {
                    filePath = actualCaller.Value.FilePath;
                    lineNumber = actualCaller.Value.LineNumber;
                    memberName = actualCaller.Value.MethodName;
                }
            }

            // Determine category based on publisher type
            var category = MvcLogContext.LogCategory.Message;
            if (publisher is MvcCommandBase)
            {
                category = MvcLogContext.LogCategory.Command;
            }
            else if (publisher is MediatorBehaviour)
            {
                category = MvcLogContext.LogCategory.Mediator;
            }
            else if (publisher is Proxy || publisher is ProxyBehaviour)
            {
                category = MvcLogContext.LogCategory.Proxy;
            }

            var moduleType = module != null ? module.GetType() : null;

            var context = new MvcLogContext(
                filePath,
                lineNumber,
                memberName,
                messageTypeName,
                sourceTypeName,
                null,
                moduleType,
                category);

            // Format: [Actor] published <Message>
            var message = $"[{sourceTypeName}] published <{messageTypeName}>";

            // Log to primary logger
            _logger.Log(message, context);
            MvcPluginBus.FireLog(message, context);
        }

        /// <summary>
        /// Log message itself (to navigate to message definition).
        /// </summary>
        [Conditional("UNITY_EDITOR"), Conditional("MVC_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogMessageDispatched<TMessage>(mvcExpress.MvcModule module)
        {
            if (!ShouldLog(module)) return;

            var messageType = typeof(TMessage);
            var messageTypeName = messageType.Name;

            var filePath = TryGetTypeFilePath(messageType);
            var lineNumber = 0;

            var moduleType = module != null ? module.GetType() : null;

            var context = new MvcLogContext(
                filePath,
                lineNumber,
                "",
                messageTypeName,
                null,
                null,
                moduleType,
                MvcLogContext.LogCategory.Message);

            var message = $"<{messageTypeName}> dispatched";

            // Log to primary logger
            _logger.Log(message, context);
            MvcPluginBus.FireLog(message, context);
        }

        /// <summary>
        /// Log command execution.
        /// </summary>
        [Conditional("UNITY_EDITOR"), Conditional("MVC_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogCommandExecuting<TMessage, TCommand>(mvcExpress.MvcModule module)
        {
            if (!ShouldLog(module)) return;

            var messageTypeName = typeof(TMessage).Name;
            var commandType = typeof(TCommand);
            var commandTypeName = commandType.Name;

            var filePath = TryGetTypeFilePath(commandType);

            var moduleType = module != null ? module.GetType() : null;

            var context = new MvcLogContext(
                filePath,
                0,
                "",
                messageTypeName,
                null,
                commandTypeName,
                moduleType,
                MvcLogContext.LogCategory.Command);

            // Format: <Message> executes [Command]
            var message = $"<{messageTypeName}> executes [{commandTypeName}]";

            // Log to primary logger
            _logger.Log(message, context);
            MvcPluginBus.FireLog(message, context);
        }

        // ========== MODULE LIFECYCLE ==========

        /// <summary>
        /// Log module initialization started.
        /// </summary>
        [Conditional("UNITY_EDITOR"), Conditional("MVC_LOGGING")]
        public static void LogModuleInitializationStarted(mvcExpress.MvcModule module, UnityEngine.Object moduleObject)
        {
            // Check both global and per-module logging
            if (!ShouldLog(module)) return;

            var moduleType = module != null ? module.GetType() : null;
            var moduleName = moduleType != null ? moduleType.Name : "Unknown";

            var context = new MvcLogContext(
                null, 0, null,
                moduleType: moduleType,
                category: MvcLogContext.LogCategory.Module,
                unityObject: moduleObject);

            var message = $"Module [[{moduleName}]] initialization started";

            _logger.Log(message, context);
            MvcPluginBus.FireLog(message, context);
        }

        /// <summary>
        /// Log module initialization completed.
        /// </summary>
        [Conditional("UNITY_EDITOR"), Conditional("MVC_LOGGING")]
        public static void LogModuleInitializationCompleted(mvcExpress.MvcModule module, UnityEngine.Object moduleObject, float elapsedSeconds)
        {
            // Check both global and per-module logging
            if (!ShouldLog(module)) return;

            var moduleType = module != null ? module.GetType() : null;
            var moduleName = moduleType != null ? moduleType.Name : "Unknown";

            var context = new MvcLogContext(
                null, 0, null,
                moduleType: moduleType,
                category: MvcLogContext.LogCategory.Module,
                unityObject: moduleObject);

            var message = $"Module [[{moduleName}]] initialization completed ({elapsedSeconds:F3}s)";

            _logger.Log(message, context);
            MvcPluginBus.FireLog(message, context);
        }

        // ========== SERVICE REGISTRATION ==========

        /// <summary>
        /// Log service registration.
        /// </summary>
        [Conditional("UNITY_EDITOR"), Conditional("MVC_LOGGING")]
        public static void LogServiceRegistered(
            string serviceTypeName,
            mvcExpress.MvcModule module,
            MvcLogContext.RegistrationSource source,
            UnityEngine.Object unityObject = null,
            string filePath = null,
            int lineNumber = 0,
            string callerTypeName = null)
        {
            if (!ShouldLog(module)) return;

            string sourceText;
            if (!string.IsNullOrEmpty(callerTypeName))
            {
                // Registered by specific actor (command, etc.)
                sourceText = $"by [{callerTypeName}]";
            }
            else if (source == MvcLogContext.RegistrationSource.Unity)
            {
                sourceText = "using registry";
            }
            else if (source == MvcLogContext.RegistrationSource.Attribute)
            {
                sourceText = "via attribute";
            }
            else
            {
                sourceText = "by code";
            }

            var moduleType = module != null ? module.GetType() : null;

            var context = new MvcLogContext(
                filePath, lineNumber, "RegisterServices",
                moduleType: moduleType,
                category: MvcLogContext.LogCategory.Service,
                source: source,
                targetTypeName: serviceTypeName,
                unityObject: unityObject);

            var message = $"Service [{serviceTypeName}] registered {sourceText}";

            _logger.Log(message, context);
            MvcPluginBus.FireLog(message, context);
        }

        // ========== PROXY REGISTRATION ==========

        /// <summary>
        /// Log proxy registration.
        /// </summary>
        [Conditional("UNITY_EDITOR"), Conditional("MVC_LOGGING")]
        public static void LogProxyRegistered(
            string proxyTypeName,
            mvcExpress.MvcModule module,
            MvcLogContext.RegistrationSource source,
            UnityEngine.Object unityObject = null,
            string filePath = null,
            int lineNumber = 0,
            string callerTypeName = null)
        {
            if (!ShouldLog(module)) return;

            string sourceText;
            if (!string.IsNullOrEmpty(callerTypeName))
            {
                // Registered by specific actor (command, etc.)
                sourceText = $"by [{callerTypeName}]";
            }
            else if (source == MvcLogContext.RegistrationSource.Unity)
            {
                sourceText = "using registry";
            }
            else if (source == MvcLogContext.RegistrationSource.Attribute)
            {
                sourceText = "via attribute";
            }
            else
            {
                sourceText = "by code";
            }

            var moduleType = module != null ? module.GetType() : null;

            var context = new MvcLogContext(
                filePath, lineNumber, "RegisterProxies",
                moduleType: moduleType,
                category: MvcLogContext.LogCategory.Proxy,
                source: source,
                targetTypeName: proxyTypeName,
                unityObject: unityObject);

            var message = $"Proxy [{proxyTypeName}] registered {sourceText}";

            _logger.Log(message, context);
            MvcPluginBus.FireLog(message, context);
        }

        // ========== COMMAND BINDING ==========

        /// <summary>
        /// Log command binding.
        /// </summary>
        [Conditional("UNITY_EDITOR"), Conditional("MVC_LOGGING")]
        public static void LogCommandBound(
            string messageTypeName,
            string commandTypeName,
            mvcExpress.MvcModule module,
            MvcLogContext.RegistrationSource source,
            UnityEngine.Object unityObject = null,
            string filePath = null,
            int lineNumber = 0)
        {
            if (!ShouldLog(module)) return;
            if (source == MvcLogContext.RegistrationSource.Code && module != null && module.SuppressCommandBindingLog) return;

            var sourceText = source == MvcLogContext.RegistrationSource.Unity
                ? "using registry"
                : source == MvcLogContext.RegistrationSource.Attribute
                    ? "via attribute"
                    : "by code";

            var moduleType = module != null ? module.GetType() : null;

            var context = new MvcLogContext(
                filePath, lineNumber, "BindCommands",
                messageTypeName: messageTypeName,
                commandTypeName: commandTypeName,
                moduleType: moduleType,
                category: MvcLogContext.LogCategory.Command,
                source: source,
                unityObject: unityObject);

            var message = $"Command [{commandTypeName}] bound to <{messageTypeName}> {sourceText}";

            _logger.Log(message, context);
            MvcPluginBus.FireLog(message, context);
        }

        /// <summary>
        /// Log command unbinding.
        /// </summary>
        [Conditional("UNITY_EDITOR"), Conditional("MVC_LOGGING")]
        public static void LogCommandUnbound(
            string messageTypeName,
            string commandTypeName,
            mvcExpress.MvcModule module,
            string filePath = null,
            int lineNumber = 0)
        {
            if (!ShouldLog(module)) return;

            var moduleType = module != null ? module.GetType() : null;

            var context = new MvcLogContext(
                filePath, lineNumber, "UnbindCommand",
                messageTypeName: messageTypeName,
                commandTypeName: commandTypeName,
                moduleType: moduleType,
                category: MvcLogContext.LogCategory.Command,
                source: MvcLogContext.RegistrationSource.Code);

            var message = $"Command [{commandTypeName}] unbound from <{messageTypeName}>";

            _logger.Log(message, context);
            MvcPluginBus.FireLog(message, context);
        }

        // ========== MEDIATOR ATTACHMENT ==========

        /// <summary>
        /// Log mediator attachment.
        /// </summary>
        [Conditional("UNITY_EDITOR"), Conditional("MVC_LOGGING")]
        public static void LogMediatorAttached(
            string mediatorTypeName,
            string gameObjectName,
            mvcExpress.MvcModule module,
            MvcLogContext.RegistrationSource source,
            UnityEngine.Object mediatorObject,
            string filePath = null,
            int lineNumber = 0,
            bool isPrefab = false)
        {
            if (!ShouldLog(module)) return;

            // Clean up GameObject name - remove " (Mediator)" suffix if present
            var cleanName = gameObjectName;
            if (cleanName.EndsWith(" (Mediator)"))
            {
                cleanName = cleanName.Substring(0, cleanName.Length - " (Mediator)".Length);
            }

            string sourceText;
            if (source == MvcLogContext.RegistrationSource.Unity)
            {
                sourceText = "attached using registry";
            }
            else if (source == MvcLogContext.RegistrationSource.Attribute)
            {
                sourceText = isPrefab ? "prefab attached via attribute" : "attached via attribute";
            }
            else if (isPrefab)
            {
                sourceText = "prefab attached by code";
            }
            else
            {
                sourceText = "attached by code";
            }

            var moduleType = module != null ? module.GetType() : null;

            var context = new MvcLogContext(
                filePath, lineNumber, "AttachMediators",
                moduleType: moduleType,
                category: MvcLogContext.LogCategory.Mediator,
                source: source,
                targetTypeName: mediatorTypeName,
                unityObject: mediatorObject);

            var message = $"Mediator [{mediatorTypeName}] {sourceText} to {{{cleanName}}}";

            _logger.Log(message, context);
            MvcPluginBus.FireLog(message, context);
        }

        // ========== MESSAGE HANDLING ==========

        /// <summary>
        /// Log message handled by mediator/proxy/command.
        /// </summary>
        [Conditional("UNITY_EDITOR"), Conditional("MVC_LOGGING")]
        public static void LogMessageHandled(
            string handlerTypeName,
            string messageTypeName,
            mvcExpress.MvcModule module,
            MvcLogContext.LogCategory category,
            string handlerMethodName = null,
            string filePath = null,
            int lineNumber = 0)
        {
            if (!ShouldLog(module)) return;

            var moduleType = module != null ? module.GetType() : null;

            var context = new MvcLogContext(
                filePath, lineNumber, handlerMethodName ?? "Handle",
                messageTypeName: messageTypeName,
                sourceTypeName: handlerTypeName,
                moduleType: moduleType,
                category: category);

            var message = $"[{handlerTypeName}] handling <{messageTypeName}>";

            _logger.Log(message, context);
            MvcPluginBus.FireLog(message, context);
        }

        // ========== COMMAND-TO-COMMAND EXECUTION ==========

        /// <summary>
        /// Log command executed from another command.
        /// </summary>
        [Conditional("UNITY_EDITOR"), Conditional("MVC_LOGGING")]
        public static void LogCommandExecutedFromCommand(
            string parentCommandTypeName,
            string childCommandTypeName,
            mvcExpress.MvcModule module,
            string filePath = null,
            int lineNumber = 0)
        {
            if (!ShouldLog(module)) return;

            var moduleType = module != null ? module.GetType() : null;

            var context = new MvcLogContext(
                filePath, lineNumber, "Execute",
                sourceTypeName: parentCommandTypeName,
                commandTypeName: childCommandTypeName,
                moduleType: moduleType,
                category: MvcLogContext.LogCategory.Command);

            var message = $"[{parentCommandTypeName}] executes [{childCommandTypeName}]";

            _logger.Log(message, context);
            MvcPluginBus.FireLog(message, context);
        }

        /// <summary>
        /// Log command executed directly via Run().
        /// </summary>
        [Conditional("UNITY_EDITOR"), Conditional("MVC_LOGGING")]
        public static void LogCommandRun(
            string commandTypeName,
            mvcExpress.MvcModule module,
            string filePath = null,
            int lineNumber = 0)
        {
            if (!ShouldLog(module)) return;

            var moduleType = module != null ? module.GetType() : null;
            var moduleName = moduleType != null ? moduleType.Name : "Unknown";

            var context = new MvcLogContext(
                filePath, lineNumber, "OnInitialized",
                commandTypeName: commandTypeName,
                moduleType: moduleType,
                category: MvcLogContext.LogCategory.Command);

            var message = $"Module [[{moduleName}]] runs [{commandTypeName}]";

            _logger.Log(message, context);
            MvcPluginBus.FireLog(message, context);
        }

        // ========== CUSTOM USER OUTPUT (MvcDebug) ==========

        /// <summary>
        /// Public method used by MvcDebug for custom user logging.
        /// Not intended for direct use - use MvcDebug.Log() instead.
        /// </summary>
#if UNITY_2023_1_OR_NEWER
        [HideInCallstack]
#endif
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_INCLUDE_TESTS"), Conditional("MVC_LOGGING")]
        public static void InternalLog(string message, MvcLogContext context)
        {
            _logger.Log(message, context);
            MvcPluginBus.FireLog(message, context);
        }

        /// <summary>
        /// Public method used by MvcDebug for custom user warnings.
        /// Not intended for direct use - use MvcDebug.LogWarning() instead.
        /// </summary>
#if UNITY_2023_1_OR_NEWER
        [HideInCallstack]
#endif
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_INCLUDE_TESTS"), Conditional("MVC_LOGGING")]
        public static void InternalLogWarning(string message, MvcLogContext context)
        {
            _logger.LogWarning(message, context);
            MvcPluginBus.FireWarning(message, context);
        }

        /// <summary>
        /// Public method used by MvcDebug for custom user errors. Stripped in clean release builds.
        /// Not intended for direct use - use MvcDebug.LogError() instead.
        /// </summary>
#if UNITY_2023_1_OR_NEWER
        [HideInCallstack]
#endif
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_INCLUDE_TESTS"), Conditional("MVC_LOGGING")]
        public static void InternalLogError(string message, MvcLogContext context)
        {
            _logger.LogError(message, context);
            MvcPluginBus.FireError(message, context);
        }

        // Returns true if either the global flag or the module-specific flag is set.
        // Inline hint because this is called at the top of every log method.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldLog(mvcExpress.MvcModule module)
        {
            if (_loggingEnabled)
                return true;

            if (module != null)
                return module.LoggingEnabled;

            return false;
        }

        // Editor-only: resolves a file system path for a type so the console can display a
        // clickable link that opens the type's source file. Fails silently - navigation is a
        // convenience feature, not a correctness requirement.
        private static string TryGetTypeFilePath(System.Type type)
        {
#if UNITY_EDITOR
            if (type == null)
                return string.Empty;

            try
            {
                // For MonoBehaviour/ScriptableObject types, try to find the script asset
                if (typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(type))
                {
                    // Use UnityEditor.MonoScript to find the script file
                    // Use FindAnyObjectByType for Unity 2023.1+ compatibility without relying on instance ID ordering.
#if UNITY_2023_1_OR_NEWER
                    var monoScript = UnityEditor.MonoScript.FromMonoBehaviour(
                        UnityEngine.Object.FindAnyObjectByType(type) as UnityEngine.MonoBehaviour);
#else
                    var monoScript = UnityEditor.MonoScript.FromMonoBehaviour(
                        UnityEngine.Object.FindObjectOfType(type) as UnityEngine.MonoBehaviour);
#endif

                    if (monoScript != null)
                    {
                        var assetPath = UnityEditor.AssetDatabase.GetAssetPath(monoScript);
                        if (!string.IsNullOrEmpty(assetPath))
                        {
                            return System.IO.Path.GetFullPath(assetPath);
                        }
                    }
                }
                else if (typeof(UnityEngine.ScriptableObject).IsAssignableFrom(type))
                {
                    var scriptableObject = UnityEngine.ScriptableObject.CreateInstance(type);
                    if (scriptableObject != null)
                    {
                        var monoScript = UnityEditor.MonoScript.FromScriptableObject(scriptableObject);
                        UnityEngine.Object.DestroyImmediate(scriptableObject);

                        if (monoScript != null)
                        {
                            var assetPath = UnityEditor.AssetDatabase.GetAssetPath(monoScript);
                            if (!string.IsNullOrEmpty(assetPath))
                            {
                                return System.IO.Path.GetFullPath(assetPath);
                            }
                        }
                    }
                }

                // For any type (including non-Unity types), search for the script file by name
                // This works for messages, commands, and other plain C# types
                var typeName = type.Name;
                var guids = UnityEditor.AssetDatabase.FindAssets($"t:MonoScript {typeName}");

                for (int i = 0; i < guids.Length; i++)
                {
                    var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                    var monoScript = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.MonoScript>(assetPath);

                    if (monoScript != null && monoScript.GetClass() == type)
                    {
                        return System.IO.Path.GetFullPath(assetPath);
                    }
                }

                // Fallback: try to find by filename pattern (TypName.cs)
                var searchPattern = $"{typeName} t:MonoScript";
                guids = UnityEditor.AssetDatabase.FindAssets(searchPattern);

                if (guids.Length > 0)
                {
                    var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    if (assetPath.EndsWith($"{typeName}.cs", System.StringComparison.OrdinalIgnoreCase))
                    {
                        return System.IO.Path.GetFullPath(assetPath);
                    }
                }
            }
            catch
            {
                // If any error occurs during editor-only resolution, fail silently
                // This is acceptable because navigation is a convenience feature
            }
#endif
            return string.Empty;
        }

        private struct CallerInfo
        {
            /// <summary>
            /// Source file path resolved from the stack frame.
            /// </summary>
            public string FilePath;

            /// <summary>
            /// Source line number resolved from the stack frame.
            /// </summary>
            public int LineNumber;

            /// <summary>
            /// Method name resolved from the stack frame.
            /// </summary>
            public string MethodName;
        }

        /// <summary>
        /// Find the actual user code caller by walking the stack trace.
        /// Skips framework code (mvcExpress namespace).
        /// </summary>
        private static CallerInfo? FindActualCaller()
        {
#if UNITY_EDITOR || MVC_LOGGING
            try
            {
                var stackTrace = new StackTrace(true);
                var frames = stackTrace.GetFrames();

                if (frames == null || frames.Length == 0)
                    return null;

                // Walk up the stack to find first frame outside mvcExpress namespace
                for (int i = 0; i < frames.Length; i++)
                {
                    var frame = frames[i];
                    var method = frame.GetMethod();

                    if (method == null || method.DeclaringType == null)
                        continue;

                    var declaringType = method.DeclaringType;
                    var ns = declaringType.Namespace ?? "";

                    // Skip framework internals
                    if (ns.StartsWith("mvcExpress"))
                        continue;

                    // Skip Unity internals
                    if (ns.StartsWith("UnityEngine") || ns.StartsWith("Unity."))
                        continue;

                    // Found user code!
                    var fileName = frame.GetFileName();
                    var lineNumber = frame.GetFileLineNumber();

                    if (!string.IsNullOrEmpty(fileName) && lineNumber > 0)
                    {
                        return new CallerInfo
                        {
                            FilePath = fileName,
                            LineNumber = lineNumber,
                            MethodName = method.Name
                        };
                    }
                }
            }
            catch
            {
                // Stack trace not available or error - fall back to caller attributes
            }
#endif
            return null;
        }
    }
}
