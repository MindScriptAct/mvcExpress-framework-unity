using mvcExpress;
using mvcExpress.Internal.Interfaces;
using mvcExpress.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace mvcExpress.Internal.Messaging
{
    /// <summary>
    /// Value-type handle returned by <see cref="MvcMessageBus.Subscribe{TMessage}"/> and used
    /// to unsubscribe without holding a reference to the delegate.
    /// </summary>
    /// <remarks>
    /// Stored as a struct (no heap allocation, no IDisposable).
    /// <see cref="Handle"/> is a stable identity, independent of where the subscription's handler
    /// currently sits in storage - it stays valid even if compaction physically moves the handler
    /// to a different array slot to reclaim space left by someone else's Unsubscribe.
    /// <see cref="Version"/> is a monotonic counter that invalidates a token once its handle has
    /// been freed and (possibly) reissued to a different subscriber. Debug fields are compiled out
    /// in production to keep the struct lean.
    /// </remarks>
    public struct SubscriptionToken
    {
        internal int Handle;
        internal int Version;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public string DebugName;
        public string SubscriberTypeName;
        public float SubscribeTime;
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal SubscriptionToken(int handle, int version)
        {
            Handle = handle;
            Version = version;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DebugName = null;
            SubscriberTypeName = null;
            SubscribeTime = 0f;
#endif
        }
    }

    /// <summary>
    /// The single typed, zero-allocation publish/subscribe message bus for the application.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Fundamental architecture invariant:</b> <see cref="MvcFacade"/> owns exactly one
    /// <see cref="MvcMessageBus"/>, and every module and actor uses that instance. Publishing is
    /// always application-wide. Module-local buses, target-module delivery, message bridges, and
    /// per-module filtering are intentionally not supported.
    /// </para>
    /// Design goals (in priority order):
    /// <list type="number">
    ///   <item><description>Zero GC per publish - no boxing, no delegate wrappers, no IDisposable tokens.</description></item>
    ///   <item><description>O(n) publish where n is the subscriber count for that message type.</description></item>
    ///   <item><description>App-wide scope - the single bus is shared by all modules via <see cref="MvcFacade"/>;
    ///     a message published in any module is delivered to every subscriber regardless of which module they belong to.</description></item>
    /// </list>
    ///
    /// Storage layout - static-per-generic-type:
    /// Each <c>Storage0&lt;TMessage&gt;</c> (and analogous Storage1…Storage12) is a static nested
    /// class parameterised on the message type. It directly holds the handler array and associated
    /// counters for the single application bus, so dispatch requires no dictionary lookup or bus-ID
    /// indexing.
    ///
    /// Arity variants (0-12 payload parameters) are generated mechanically in the
    /// <c>MvcMessageBus.Params00.cs</c> … <c>Params12.cs</c> partial files.
    ///
    /// Thread safety:
    /// - Subscribe, Unsubscribe, UnsubscribeAll, and Publish are all main-thread-only. This is a
    ///   single, uniform contract - no build has stronger correctness guarantees than another,
    ///   only different validation cost.
    /// - In Editor/Development builds, that contract is validated via <c>ValidateMainThread()</c>:
    ///   calling any of Subscribe/Unsubscribe/UnsubscribeAll/Publish from a background thread throws
    ///   <see cref="System.InvalidOperationException"/>.
    /// - In release builds, validation is compiled out (zero-cost) - the contract still holds, it is
    ///   just unvalidated.
    /// - Use <c>MessengerApi.PublishDeferred</c> for off-thread delivery - it enqueues the action onto
    ///   <see cref="MvcFacade"/>'s per-frame drain queue and delivers on the main thread.
    ///
    /// Subscriber exceptions (MSG-01/DEC-11's sibling finding, MSG-02/DEC-04):
    /// - In Editor/Development builds, each subscriber invocation inside <c>Publish</c>'s loop is
    ///   individually try/caught and logged via <c>MvcDebug.LogError</c> - one throwing subscriber
    ///   does not prevent later subscribers from receiving the same publish.
    /// - In release builds, this is compiled out entirely (zero cost, byte-for-byte the same loop as
    ///   before): a throwing subscriber propagates and stops later subscribers, exactly like today.
    ///   This is a deliberate, permanent asymmetry - not an oversight like the one MSG-05 closed for
    ///   thread validation - chosen because <c>Publish</c> is the framework's single hottest path and
    ///   isolating every dispatch would add a small but permanent cost to every release build, for a
    ///   failure mode (a subscriber throwing) that should be caught during development. A subscriber
    ///   with a known failure risk in production should still guard itself with its own try/catch.
    /// - Command-bound subscribers are unaffected either way: <c>MvcCommandProcessor</c> already
    ///   wraps every command's execution in its own always-on (every build) try/catch, independent of
    ///   this setting - see <c>MvcCommandProcessor.RaiseCommandFault</c>.
    ///
    /// Dispose/cleanup:
    /// - <see cref="Dispose()"/> clears the application's handler slots.
    /// - No finalizer: the bus is application-lifetime; <see cref="MvcFacade"/> always calls <see cref="Dispose()"/> explicitly.
    ///
    /// This class is <c>public</c> (not <c>internal</c>) because the partial files and
    /// <see cref="SubscriptionToken"/> must be accessible from other framework assemblies,
    /// but it should not be used directly by application code - use the actor base classes
    /// (<c>Proxy</c>, <c>Command</c>, <c>MediatorBehaviour</c>) which expose
    /// <c>Publish</c> / <c>Subscribe</c> wrappers.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed partial class MvcMessageBus : IMessageBus, IDisposable
    {
        // Initial slot count for handler arrays. Keep small to avoid wasting memory for
        // message types with few subscribers; arrays are doubled on overflow up to MAX_POOL_SIZE.
        private const int INITIAL_CAPACITY = 8;
        // Absolute cap on handler array length. Beyond this, each new subscriber still gets a slot
        // (the array keeps growing) but pool sizes advertised to callers are capped here.
        private const int MAX_POOL_SIZE = 64;

        // Sentinel meaning "no free handle" in a storage handle free-list head.
        private const int NO_FREE_SLOT = -1;

        // Handle free-list machinery shared by every Storage<N> class (Params00-Params12).
        // Each Storage<N>.HandleFreeListHead is the most recently freed handle number (a
        // singly-linked free list, LIFO), or NO_FREE_SLOT if none; HandleNextFree[handle] holds
        // the link to the next-most-recently-freed handle. This lets Subscribe reuse a handle
        // number without ever touching the dense Handlers[] array - handle reuse and data-slot
        // placement are fully independent, which is what keeps reentrant-subscribe-during-publish
        // safe (MSG-01) even though handles get recycled. VersionCounter is `int` and increments
        // once per (re)allocated handle with no wraparound handling; overflow would require ~2
        // billion subscribes on a single message type - theoretical, but noted here rather than
        // silently assumed.

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Captured once at class-load time. Used by ValidateMainThread() to detect off-thread publishes.
        //
        // This is also the framework's one authoritative "main thread" reference - reused by
        // BoundedObjectPool (PERF-07 follow-up) instead of each pool capturing whichever thread
        // happens to construct it. That per-instance capture was wrong whenever a pool's first
        // construction was itself off-thread (e.g. Commander.Run<TCommand>() called from a
        // background thread before the command was ever Bind-ed): the pool would then correctly
        // reject the real main thread forever after. This field is guaranteed initialized on the
        // real main thread before any pool can exist, because MvcFacade's constructor
        // (`new MvcMessageBus()`, the first-ever touch of this type) always runs during
        // MvcModule.Awake() - a Unity lifecycle callback - and no command pool can be reached
        // without a module (and therefore a facade) already existing.
        private static readonly int _mainThreadId = Thread.CurrentThread.ManagedThreadId;

        /// <summary>
        /// The framework's authoritative main-thread ID, for other internal components that need
        /// the same "am I on the main thread" check <see cref="ValidateMainThread"/> uses. Editor/
        /// Development builds only, matching every other main-thread-validation mechanism here.
        /// </summary>
        internal static int MainThreadId => _mainThreadId;
#endif

        // Tracks which message types the application bus has ever subscribed to, so Dispose can
        // iterate exactly those types during ClearStorage without scanning all possible types.
        // ConcurrentDictionary because subscriptions can theoretically occur from any thread;
        // byte value is irrelevant - only the key set matters.
        private readonly ConcurrentDictionary<Type, byte> _usedMessageTypes = new ConcurrentDictionary<Type, byte>();

        private bool _disposed;
        // Guards against re-logging the single whole-bus Dispose() failure (not tied to any
        // one message type) more than once per cleanup pass.
        private bool _disposeCleanupErrorLogged;
        // Tracks which specific types (message types or storage types) have already logged a
        // cleanup warning/error, so one failing type doesn't silence diagnostics for every other
        // type during the same Dispose() pass.
        private readonly HashSet<Type> _perTypeCleanupErrorLogged = new HashSet<Type>();

        // Maps each IMessage<...> generic type definition to its payload arity (0-12).
        // Read-only after static initialisation; safe to read from any thread without locking.
        private static readonly IReadOnlyDictionary<Type, int> KnownMessageInterfaces = 
            new Dictionary<Type, int>
            {
                { typeof(IMessage), 0 },
                { typeof(IMessage<>), 1 },
                { typeof(IMessage<,>), 2 },
                { typeof(IMessage<,,>), 3 },
                { typeof(IMessage<,,,>), 4 },
                { typeof(IMessage<,,,,>), 5 },
                { typeof(IMessage<,,,,,>), 6 },
                { typeof(IMessage<,,,,,,>), 7 },
                { typeof(IMessage<,,,,,,,>), 8 },
                { typeof(IMessage<,,,,,,,,>), 9 },
                { typeof(IMessage<,,,,,,,,,>), 10 },
                { typeof(IMessage<,,,,,,,,,,>), 11 },
                { typeof(IMessage<,,,,,,,,,,,>), 12 }
            };

        // Caches message type validation results so the reflection check (GetInterfaces scan) is
        // performed only once per type. Active only in Editor/Development builds - zero overhead in
        // production (the entire class is replaced by a no-op stub via #else).
        private static class MessageValidationCache
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            private static readonly HashSet<Type> _validatedTypes = new HashSet<Type>();
            private static readonly object _validationLock = new object();

            /// <summary>
            /// Validates that a message type implements exactly one IMessage interface.
            /// Logs warnings if multiple interfaces are detected (Editor/Development only).
            /// Results are cached to avoid repeated reflection.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void ValidateMessageType(Type messageType)
            {
                // Always locked - HashSet<T> is not thread-safe, so an unsynchronized "fast path"
                // read here would race a concurrent Add(); dev-only code, so the lock's cost is
                // irrelevant.
                lock (_validationLock)
                {
                    if (_validatedTypes.Contains(messageType))
                    {
                        return;
                    }

                    // Collect all IMessage interfaces
                    var messageInterfaces = messageType.GetInterfaces()
                        .Where(i => i == typeof(IMessage) || 
                                    (i.IsGenericType && KnownMessageInterfaces.ContainsKey(i.GetGenericTypeDefinition())))
                        .ToList();

                    // Warn about multiple interfaces (edge case but legal)
                    if (messageInterfaces.Count > 1)
                    {
                        var interfaceNames = string.Join(", ", messageInterfaces.Select(i => i.Name));
                        MvcDebug.LogWarning(
                            $"Message type '{messageType.Name}' implements multiple IMessage interfaces ({interfaceNames}).\n" +
                            "This is an unusual pattern and may lead to unexpected behavior during cleanup.\n" +
                            "Consider refactoring to use separate message types for different payloads.\n" +
                            "This warning only appears in Editor/Development builds."
                        );
                    }

                    // Warn about missing interface
                    if (messageInterfaces.Count == 0)
                    {
                        MvcDebug.LogWarning(
                            $"Message type '{messageType.Name}' does not implement any IMessage interface.\n" +
                            "Messages must implement IMessage or IMessage<...> to be used with the message bus.\n" +
                            "This warning only appears in Editor/Development builds."
                        );
                    }

                    // Mark as validated (even if warnings were logged)
                    _validatedTypes.Add(messageType);
                }
            }
#else
            /// <summary>
            /// No-op in production builds - validation is Editor/Development only.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void ValidateMessageType(Type messageType)
            {
                // No validation in production
            }
#endif
        }

        // Caches the FieldInfo references needed to clear a Storage<TMessage> slot during Dispose.
        // Without this cache, Dispose would call GetField() for every message type on every call -
        // expensive for modules with many distinct message types.
        private static class ReflectionCache
        {
            private static readonly Dictionary<Type, StorageFieldInfo> _cache = new Dictionary<Type, StorageFieldInfo>();
            private static readonly object _cacheLock = new object();

            internal class StorageFieldInfo
            {
                public FieldInfo LockField;
                public FieldInfo HandlersField;
                public FieldInfo SlotOwnerField;
                public FieldInfo CountField;
                public FieldInfo LiveCountField;
                public FieldInfo HasPendingHolesField;
                public FieldInfo HandleToSlotField;
                public FieldInfo HandleVersionField;
                public FieldInfo HandleNextFreeField;
                public FieldInfo HandleCountField;
                public FieldInfo HandleFreeListHeadField;
                public FieldInfo VersionCounterField;
                public FieldInfo PublishDepthField;
            }

            public static StorageFieldInfo GetOrCache(Type storageType)
            {
                if (_cache.TryGetValue(storageType, out var cached))
                {
                    return cached;
                }

                lock (_cacheLock)
                {
                    if (_cache.TryGetValue(storageType, out cached))
                    {
                        return cached;
                    }

                    var info = new StorageFieldInfo
                    {
                        LockField = storageType.GetField("Lock", BindingFlags.NonPublic | BindingFlags.Static),
                        HandlersField = storageType.GetField("Handlers", BindingFlags.NonPublic | BindingFlags.Static),
                        SlotOwnerField = storageType.GetField("SlotOwner", BindingFlags.NonPublic | BindingFlags.Static),
                        CountField = storageType.GetField("Count", BindingFlags.NonPublic | BindingFlags.Static),
                        LiveCountField = storageType.GetField("LiveCount", BindingFlags.NonPublic | BindingFlags.Static),
                        HasPendingHolesField = storageType.GetField("HasPendingHoles", BindingFlags.NonPublic | BindingFlags.Static),
                        HandleToSlotField = storageType.GetField("HandleToSlot", BindingFlags.NonPublic | BindingFlags.Static),
                        HandleVersionField = storageType.GetField("HandleVersion", BindingFlags.NonPublic | BindingFlags.Static),
                        HandleNextFreeField = storageType.GetField("HandleNextFree", BindingFlags.NonPublic | BindingFlags.Static),
                        HandleCountField = storageType.GetField("HandleCount", BindingFlags.NonPublic | BindingFlags.Static),
                        HandleFreeListHeadField = storageType.GetField("HandleFreeListHead", BindingFlags.NonPublic | BindingFlags.Static),
                        VersionCounterField = storageType.GetField("VersionCounter", BindingFlags.NonPublic | BindingFlags.Static),
                        PublishDepthField = storageType.GetField("PublishDepth", BindingFlags.NonPublic | BindingFlags.Static)
                    };

                    _cache[storageType] = info;
                    return info;
                }
            }
        }

        internal MvcMessageBus() { }

        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                ClearStorage();
            }
            catch (Exception ex)
            {
                // A teardown exception must be visible even in a clean release build, unlike
                // routine diagnostic logging, so it also always goes through Unity's own
                // Debug.LogException (bypassing MvcDebug's stripping) rather than being discarded.
                if (!_disposeCleanupErrorLogged)
                {
                    MvcDebug.LogError($"Critical error during message bus disposal cleanup: {ex.Message}");
                    UnityEngine.Debug.LogException(ex);
                    _disposeCleanupErrorLogged = true;
                }
            }

            if (disposing)
            {
                _usedMessageTypes.Clear();
            }
        }

        private void ClearStorage()
        {
            // ConcurrentDictionary.Keys is thread-safe
            foreach (var messageType in _usedMessageTypes.Keys)
            {
                ClearStorageForMessageType(messageType);
            }
        }

        // IMPROVEMENT 5: No longer an edge case - validation ensures only one interface
        private void ClearStorageForMessageType(Type messageType)
        {
            try
            {
                var messageInterfaces = messageType.GetInterfaces();
                Type payloadInterface = null;
                int paramCount = -1;
                
                // Since we validate on subscribe, we know there's exactly one IMessage interface
                foreach (var iface in messageInterfaces)
                {
                    if (!iface.IsGenericType)
                    {
                        if (KnownMessageInterfaces.TryGetValue(iface, out var arity))
                        {
                            payloadInterface = iface;
                            paramCount = arity;
                            break; // Safe: validation guarantees only one match
                        }
                    }
                    else
                    {
                        var genericDef = iface.GetGenericTypeDefinition();
                        if (KnownMessageInterfaces.TryGetValue(genericDef, out var arity))
                        {
                            payloadInterface = iface;
                            paramCount = arity;
                            break; // Safe: validation guarantees only one match
                        }
                    }
                }

                if (payloadInterface == null || paramCount == -1)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (_perTypeCleanupErrorLogged.Add(messageType))
                    {
                        MvcDebug.LogWarning($"Could not find IMessage interface on type {messageType.Name} during cleanup.");
                    }
#endif
                    return;
                }

                var payloadTypes = paramCount == 0 ? Type.EmptyTypes : payloadInterface.GetGenericArguments();

                switch (paramCount)
                {
                    case 0: ClearStorage0(messageType); break;
                    case 1: ClearStorage1(messageType, payloadTypes); break;
                    case 2: ClearStorage2(messageType, payloadTypes); break;
                    case 3: ClearStorage3(messageType, payloadTypes); break;
                    case 4: ClearStorage4(messageType, payloadTypes); break;
                    case 5: ClearStorage5(messageType, payloadTypes); break;
                    case 6: ClearStorage6(messageType, payloadTypes); break;
                    case 7: ClearStorage7(messageType, payloadTypes); break;
                    case 8: ClearStorage8(messageType, payloadTypes); break;
                    case 9: ClearStorage9(messageType, payloadTypes); break;
                    case 10: ClearStorage10(messageType, payloadTypes); break;
                    case 11: ClearStorage11(messageType, payloadTypes); break;
                    case 12: ClearStorage12(messageType, payloadTypes); break;
                }
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (_perTypeCleanupErrorLogged.Add(messageType))
                {
                    MvcDebug.LogError($"Error clearing storage for message type {messageType?.Name ?? "null"}: {ex.Message}\n{ex.StackTrace}");
                }
#else
                _ = ex;
#endif
            }
        }

        private void ClearStorageSlot(Type storageType)
        {
            var fieldInfo = ReflectionCache.GetOrCache(storageType);

            if (fieldInfo.LockField == null || fieldInfo.HandlersField == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (_perTypeCleanupErrorLogged.Add(storageType))
                {
                    MvcDebug.LogWarning($"Could not find Lock or Handlers field on storage type {storageType.Name}.");
                }
#endif
                return;
            }

            var lockObj = fieldInfo.LockField.GetValue(null);
            lock (lockObj)
            {
                fieldInfo.HandlersField.SetValue(null, null);
                fieldInfo.SlotOwnerField?.SetValue(null, null);
                fieldInfo.CountField?.SetValue(null, 0);
                fieldInfo.LiveCountField?.SetValue(null, 0);
                fieldInfo.HasPendingHolesField?.SetValue(null, false);
                fieldInfo.HandleToSlotField?.SetValue(null, null);
                fieldInfo.HandleVersionField?.SetValue(null, null);
                fieldInfo.HandleNextFreeField?.SetValue(null, null);
                fieldInfo.HandleCountField?.SetValue(null, 0);
                fieldInfo.HandleFreeListHeadField?.SetValue(null, NO_FREE_SLOT);
                fieldInfo.VersionCounterField?.SetValue(null, 0);
                fieldInfo.PublishDepthField?.SetValue(null, 0);
            }
        }

        /// <summary>
        /// Returns whether at least one subscriber is currently attached for a no-payload message.
        /// </summary>
        /// <remarks>
        /// This overload is for zero-payload <see cref="IMessage"/> types. Arity 1-12 overloads
        /// (for <c>IMessage&lt;T1..T12&gt;</c>) live alongside <c>Publish</c>/<c>Subscribe</c> in
        /// the matching <c>MvcMessageBus.ParamsNN.cs</c> partial.
        /// <para>
        /// This reflects the live subscriber count: each <c>Unsubscribe</c> overload decrements it
        /// immediately, so this correctly returns <c>false</c> again once the last subscriber for
        /// this message type has been individually removed, not only after a full
        /// <c>UnsubscribeAll</c>.
        /// </para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasSubscribers<TMessage>() where TMessage : IMessage
        {
            // ConcurrentDictionary.ContainsKey is thread-safe
            if (!_usedMessageTypes.ContainsKey(typeof(TMessage)))
            {
                return false;
            }

            if (Storage0<TMessage>.Handlers == null)
            {
                return false;
            }

            return Storage0<TMessage>.LiveCount > 0;
        }

        #region Debug Statistics

        /// <summary>
        /// Statistics for message subscriptions.
        /// </summary>
        public readonly struct MessageSubscriptionStatistics
        {
            /// <summary>Message type name.</summary>
            public readonly string MessageTypeName;

            /// <summary>Number of parameter types in the message.</summary>
            public readonly int ParameterCount;

            /// <summary>Whether this message type has active subscriptions.</summary>
            public readonly bool HasSubscriptions;

            internal MessageSubscriptionStatistics(string messageTypeName, int parameterCount, bool hasSubscriptions)
            {
                MessageTypeName = messageTypeName;
                ParameterCount = parameterCount;
                HasSubscriptions = hasSubscriptions;
            }
        }

        /// <summary>
        /// Get statistics for all registered message types on the application bus.
        /// </summary>
        /// <returns>List of statistics for each message type.</returns>
        public List<MessageSubscriptionStatistics> GetSubscriptionStatistics()
        {
            var results = new List<MessageSubscriptionStatistics>(_usedMessageTypes.Count);

            foreach (var messageType in _usedMessageTypes.Keys)
            {
                int paramCount = GetMessageParameterCount(messageType);
                bool hasSubscriptions = HasSubscriptionsForType(messageType, paramCount);
                
                results.Add(new MessageSubscriptionStatistics(
                    messageType.Name,
                    paramCount,
                    hasSubscriptions
                ));
            }

            return results;
        }

        /// <summary>
        /// Get the total number of registered message types.
        /// </summary>
        public int GetRegisteredMessageTypeCount() => _usedMessageTypes.Count;

        /// <summary>
        /// Get aggregate subscription statistics.
        /// </summary>
        public (int TotalMessageTypes, int ActiveMessageTypes) GetAggregateSubscriptionStatistics()
        {
            int totalMessageTypes = _usedMessageTypes.Count;
            int activeMessageTypes = 0;

            foreach (var messageType in _usedMessageTypes.Keys)
            {
                int paramCount = GetMessageParameterCount(messageType);
                if (HasSubscriptionsForType(messageType, paramCount))
                {
                    activeMessageTypes++;
                }
            }

            return (totalMessageTypes, activeMessageTypes);
        }

        private int GetMessageParameterCount(Type messageType)
        {
            var interfaces = messageType.GetInterfaces();
            foreach (var iface in interfaces)
            {
                if (!iface.IsGenericType)
                {
                    if (KnownMessageInterfaces.TryGetValue(iface, out var arity))
                        return arity;
                }
                else
                {
                    var genericDef = iface.GetGenericTypeDefinition();
                    if (KnownMessageInterfaces.TryGetValue(genericDef, out var arity))
                        return arity;
                }
            }
            return 0;
        }

        private bool HasSubscriptionsForType(Type messageType, int paramCount)
        {
            // Use reflection to check the appropriate Storage class
            try
            {
                Type storageType = GetStorageTypeForParamCount(messageType, paramCount);
                if (storageType == null)
                    return false;

                var fieldInfo = ReflectionCache.GetOrCache(storageType);
                // Read the live count, not the slot high-water count - this API is the same "does
                // anyone currently care about this message type" question HasSubscribers answers,
                // just reached via reflection for arbitrary-arity debug statistics.
                if (fieldInfo.LiveCountField == null)
                    return false;

                var count = (int)fieldInfo.LiveCountField.GetValue(null);
                return count > 0;
            }
            catch
            {
                return false;
            }
        }

        private Type GetStorageTypeForParamCount(Type messageType, int paramCount)
        {
            var interfaces = messageType.GetInterfaces();
            Type payloadInterface = null;
            
            foreach (var iface in interfaces)
            {
                if (!iface.IsGenericType)
                {
                    if (KnownMessageInterfaces.TryGetValue(iface, out var arity) && arity == paramCount)
                    {
                        payloadInterface = iface;
                        break;
                    }
                }
                else
                {
                    var genericDef = iface.GetGenericTypeDefinition();
                    if (KnownMessageInterfaces.TryGetValue(genericDef, out var arity) && arity == paramCount)
                    {
                        payloadInterface = iface;
                        break;
                    }
                }
            }

            if (payloadInterface == null)
                return null;

            var payloadTypes = paramCount == 0 ? Type.EmptyTypes : payloadInterface.GetGenericArguments();

            return paramCount switch
            {
                0 => typeof(Storage0<>).MakeGenericType(messageType),
                1 => typeof(Storage1<,>).MakeGenericType(messageType, payloadTypes[0]),
                2 => typeof(Storage2<,,>).MakeGenericType(messageType, payloadTypes[0], payloadTypes[1]),
                3 => typeof(Storage3<,,,>).MakeGenericType(messageType, payloadTypes[0], payloadTypes[1], payloadTypes[2]),
                4 => typeof(Storage4<,,,,>).MakeGenericType(messageType, payloadTypes[0], payloadTypes[1], payloadTypes[2], payloadTypes[3]),
                5 => typeof(Storage5<,,,,,>).MakeGenericType(messageType, payloadTypes[0], payloadTypes[1], payloadTypes[2], payloadTypes[3], payloadTypes[4]),
                6 => typeof(Storage6<,,,,,,>).MakeGenericType(messageType, payloadTypes[0], payloadTypes[1], payloadTypes[2], payloadTypes[3], payloadTypes[4], payloadTypes[5]),
                7 => typeof(Storage7<,,,,,,,>).MakeGenericType(messageType, payloadTypes[0], payloadTypes[1], payloadTypes[2], payloadTypes[3], payloadTypes[4], payloadTypes[5], payloadTypes[6]),
                8 => typeof(Storage8<,,,,,,,,>).MakeGenericType(messageType, payloadTypes[0], payloadTypes[1], payloadTypes[2], payloadTypes[3], payloadTypes[4], payloadTypes[5], payloadTypes[6], payloadTypes[7]),
                9 => typeof(Storage9<,,,,,,,,,>).MakeGenericType(messageType, payloadTypes[0], payloadTypes[1], payloadTypes[2], payloadTypes[3], payloadTypes[4], payloadTypes[5], payloadTypes[6], payloadTypes[7], payloadTypes[8]),
                10 => typeof(Storage10<,,,,,,,,,,>).MakeGenericType(messageType, payloadTypes[0], payloadTypes[1], payloadTypes[2], payloadTypes[3], payloadTypes[4], payloadTypes[5], payloadTypes[6], payloadTypes[7], payloadTypes[8], payloadTypes[9]),
                11 => typeof(Storage11<,,,,,,,,,,,>).MakeGenericType(messageType, payloadTypes[0], payloadTypes[1], payloadTypes[2], payloadTypes[3], payloadTypes[4], payloadTypes[5], payloadTypes[6], payloadTypes[7], payloadTypes[8], payloadTypes[9], payloadTypes[10]),
                12 => typeof(Storage12<,,,,,,,,,,,,>).MakeGenericType(messageType, payloadTypes[0], payloadTypes[1], payloadTypes[2], payloadTypes[3], payloadTypes[4], payloadTypes[5], payloadTypes[6], payloadTypes[7], payloadTypes[8], payloadTypes[9], payloadTypes[10], payloadTypes[11]),
                _ => null
            };
        }

        #endregion

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Throws <see cref="System.InvalidOperationException"/> when Publish is called from a
        /// background thread. Active only in Editor/Development builds - zero overhead in production.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // [CallerMemberName] fills in the caller's own method name (Publish, Subscribe, Unsubscribe,
        // UnsubscribeAll, ...) automatically at every call site - none of them need to pass it
        // explicitly, so this stays accurate as new callers adopt the same main-thread contract.
        internal static void ValidateMainThread([CallerMemberName] string callerName = "Publish")
        {
            if (Thread.CurrentThread.ManagedThreadId != _mainThreadId)
            {
                throw new InvalidOperationException(
                    $"Messenger.{callerName} was called from a background thread " +
                    $"(thread ID {Thread.CurrentThread.ManagedThreadId}; main thread ID {_mainThreadId}).\n" +
                    "The message bus is not thread-safe - it must run on the Unity main thread.\n" +
                    "If you are calling it after Task.Run or from a background callback, use " +
                    "Messenger.PublishDeferred<TMessage>() instead to publish. " +
                    "It enqueues delivery to the next Update frame."
                );
            }
        }
#endif
    }
}

