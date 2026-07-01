﻿using mvcExpress;
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
    /// Two fields uniquely identify the slot: <see cref="Index"/> (position in the handler array)
    /// and <see cref="Version"/> (monotonic counter that invalidates stale tokens after unsubscribe).
    /// Debug fields are compiled out in production to keep the struct lean.
    /// </remarks>
    public struct SubscriptionToken
    {
        internal int Index;
        internal int Version;
        
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public string DebugName;
        public string SubscriberTypeName;
        public float SubscribeTime;
#endif
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal SubscriptionToken(int index, int version)
        {
            Index = index;
            Version = version;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DebugName = null;
            SubscriberTypeName = null;
            SubscribeTime = 0f;
#endif
        }
    }

    /// <summary>
    /// Typed, zero-allocation publish/subscribe message bus shared across the entire application.
    /// </summary>
    /// <remarks>
    /// Design goals (in priority order):
    /// <list type="number">
    ///   <item><description>Zero GC per publish - no boxing, no delegate wrappers, no IDisposable tokens.</description></item>
    ///   <item><description>O(n) publish where n is the subscriber count for that message type.</description></item>
    ///   <item><description>App-wide scope - a single bus instance is shared by all modules via <see cref="MvcFacade"/>;
    ///     a message published in any module is delivered to every subscriber regardless of which module they belong to.</description></item>
    /// </list>
    ///
    /// Storage layout - static-per-generic-type (the "instance ID trick"):
    /// Each <c>Storage0&lt;TMessage&gt;</c> (and analogous Storage1…Storage12) is a static nested
    /// class parameterised on the message type. It holds arrays of handler arrays indexed by
    /// instance ID. This means handler dispatch is a single array dereference with no dictionary
    /// lookup. Only one instance ID (0) is active at runtime; the ID and recycling machinery
    /// are preserved in case multiple buses are needed in future.
    ///
    /// Arity variants (0-12 payload parameters) are generated mechanically in the
    /// <c>MvcMessageBus.Params00.cs</c> … <c>Params12.cs</c> partial files.
    ///
    /// Thread safety:
    /// - Subscribe/Unsubscribe are guarded by the storage lock in Editor/Development builds.
    /// - Publish is intentionally lock-free in production (main-thread-only contract).
    /// - Calling Publish from a background thread throws <see cref="System.InvalidOperationException"/> in
    ///   Editor/Development builds. Use <c>MessengerApi.PublishDeferred</c> for off-thread delivery - it
    ///   enqueues the action onto <see cref="MvcFacade"/>'s per-frame drain queue and delivers on the main thread.
    ///
    /// Dispose/cleanup:
    /// - <see cref="Dispose()"/> clears all handler slots for this instance and recycles the ID.
    /// - No finalizer: the bus is process-lifetime; <see cref="MvcFacade"/> always calls <see cref="Dispose()"/> explicitly.
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

        // Index into the static Storage arrays. Always 0 for the single app-wide bus instance.
        // The ID and recycling machinery below are retained so that adding a second bus in
        // future requires no structural change - only the wiring in MvcFacade/MvcModule.
        private readonly int _instanceId;
        private static int _nextInstanceId = 0;
        private static readonly object _instanceLock = new object();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Captured once at class-load time. Used by ValidateMainThread() to detect off-thread publishes.
        private static readonly int _mainThreadId = Thread.CurrentThread.ManagedThreadId;
#endif

        // Recycled IDs from disposed bus instances. Never dequeued in the current single-bus
        // configuration (the one bus lives for the entire application session). Retained alongside
        // GetNextInstanceId/RecycleInstanceId as forward-compatible infrastructure.
        private static readonly System.Collections.Concurrent.ConcurrentQueue<int> _recycledInstanceIds =
            new System.Collections.Concurrent.ConcurrentQueue<int>();

        internal static int GetNextInstanceId()
        {
            if (_recycledInstanceIds.TryDequeue(out var recycledId))
                return recycledId;

            lock (_instanceLock)
                return _nextInstanceId++;
        }

        internal static void RecycleInstanceId(int instanceId)
        {
            if (instanceId < 0) return;
            _recycledInstanceIds.Enqueue(instanceId);
        }

        // Tracks which message types this instance has ever subscribed to, so Dispose can iterate
        // exactly those types during ClearInstanceStorage without scanning all possible types.
        // ConcurrentDictionary because subscriptions can theoretically occur from any thread;
        // byte value is irrelevant - only the key set matters.
        private readonly ConcurrentDictionary<Type, byte> _usedMessageTypes = new ConcurrentDictionary<Type, byte>();

        private bool _disposed;
        // Guards against emitting multiple error logs during a single cleanup pass.
        private bool _cleanupErrorLogged;

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
                // Fast path: already validated
                if (_validatedTypes.Contains(messageType))
                {
                    return;
                }

                lock (_validationLock)
                {
                    // Double-check after acquiring lock
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
                public FieldInfo VersionsField;
                public FieldInfo CountsField;
                public FieldInfo VersionCountersField; // ADDED: For complete cleanup
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
                        HandlersField = storageType.GetField("InstanceHandlers", BindingFlags.NonPublic | BindingFlags.Static),
                        VersionsField = storageType.GetField("InstanceVersions", BindingFlags.NonPublic | BindingFlags.Static),
                        CountsField = storageType.GetField("InstanceCounts", BindingFlags.NonPublic | BindingFlags.Static),
                        VersionCountersField = storageType.GetField("InstanceVersionCounters", BindingFlags.NonPublic | BindingFlags.Static) // ADDED
                    };

                    _cache[storageType] = info;
                    return info;
                }
            }
        }

        public MvcMessageBus()
        {
            _instanceId = GetNextInstanceId();
        }

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
                ClearInstanceStorage();
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (!_cleanupErrorLogged)
                {
                    MvcDebug.LogError($"Critical error during message bus disposal cleanup: {ex.Message}");
                    _cleanupErrorLogged = true;
                }
#else
                _ = ex;
#endif
            }

            if (disposing)
            {
                _usedMessageTypes.Clear();
                RecycleInstanceId(_instanceId);
            }
        }

        private void ClearInstanceStorage()
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
                    if (!_cleanupErrorLogged)
                    {
                        MvcDebug.LogWarning($"Could not find IMessage interface on type {messageType.Name} during cleanup.");
                        _cleanupErrorLogged = true;
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
                if (!_cleanupErrorLogged)
                {
                    MvcDebug.LogError($"Error clearing storage for message type {messageType?.Name ?? "null"}: {ex.Message}\n{ex.StackTrace}");
                    _cleanupErrorLogged = true;
                }
#else
                _ = ex;
#endif
            }
        }

        // IMPROVEMENT 4: Reset InstanceVersionCounters in addition to other fields
        private void ClearStorageSlot(Type storageType)
        {
            var fieldInfo = ReflectionCache.GetOrCache(storageType);

            if (fieldInfo.LockField == null || fieldInfo.HandlersField == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (!_cleanupErrorLogged)
                {
                    MvcDebug.LogWarning($"Could not find Lock or InstanceHandlers field on storage type {storageType.Name}.");
                    _cleanupErrorLogged = true;
                }
#endif
                return;
            }

            var lockObj = fieldInfo.LockField.GetValue(null);
            lock (lockObj)
            {
                var handlers = (Array)fieldInfo.HandlersField.GetValue(null);
                if (handlers != null && _instanceId < handlers.Length)
                {
                    handlers.SetValue(null, _instanceId);
                }

                var versions = (Array)fieldInfo.VersionsField.GetValue(null);
                if (versions != null && _instanceId < versions.Length)
                {
                    versions.SetValue(null, _instanceId);
                }

                var counts = (Array)fieldInfo.CountsField.GetValue(null);
                if (counts != null && _instanceId < counts.Length)
                {
                    counts.SetValue(0, _instanceId);
                }

                // IMPROVEMENT 4: Reset version counters to ensure full cleanup
                var versionCounters = (Array)fieldInfo.VersionCountersField?.GetValue(null);
                if (versionCounters != null && _instanceId < versionCounters.Length)
                {
                    versionCounters.SetValue(0, _instanceId);
                }
            }
        }

        /// <summary>
        /// Check if this bus instance has any active subscriptions for a message type.
        /// Useful for optimization - avoid sending messages when no one is listening.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasSubscribers<TMessage>() where TMessage : IMessage
        {
            // ConcurrentDictionary.ContainsKey is thread-safe
            if (!_usedMessageTypes.ContainsKey(typeof(TMessage)))
            {
                return false;
            }

            if (_instanceId >= Storage0<TMessage>.InstanceHandlers.Length ||
                Storage0<TMessage>.InstanceHandlers[_instanceId] == null)
            {
                return false;
            }

            return Storage0<TMessage>.InstanceCounts[_instanceId] > 0;
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
        /// Get statistics for all registered message types in this bus instance.
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
                if (fieldInfo.CountsField == null)
                    return false;

                var counts = (Array)fieldInfo.CountsField.GetValue(null);
                if (counts == null || _instanceId >= counts.Length)
                    return false;

                var count = (int)counts.GetValue(_instanceId);
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
        internal static void ValidateMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _mainThreadId)
            {
                throw new InvalidOperationException(
                    $"Messenger.Publish was called from a background thread " +
                    $"(thread ID {Thread.CurrentThread.ManagedThreadId}; main thread ID {_mainThreadId}).\n" +
                    "The message bus is not thread-safe - Publish must run on the Unity main thread.\n" +
                    "If you are publishing after Task.Run or from a background callback, use " +
                    "Messenger.PublishDeferred<TMessage>() instead. " +
                    "It enqueues delivery to the next Update frame."
                );
            }
        }
#endif
    }
}

