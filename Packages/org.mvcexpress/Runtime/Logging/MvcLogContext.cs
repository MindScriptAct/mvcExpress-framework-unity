using System;
using UnityEngine;

namespace mvcExpress.Logging
{
    /// <summary>
    /// Immutable value type bundled with every log entry to provide structured metadata
    /// beyond the plain text message.
    /// </summary>
    /// <remarks>
    /// Fields are optional; unused fields default to <c>null</c> / <c>default</c>.
    /// Log consumers (<see cref="IMvcLogger"/>, <see cref="IMvcLoggerPlugin"/>) use these
    /// fields to build clickable navigation, filtering, and timeline visualisation in editor tools.
    ///
    /// <see cref="UnityObject"/> is the Unity object associated with the log entry, used for
    /// hierarchy pings. It is <c>null</c> for purely code-originated log entries.
    ///
    /// <see cref="Source"/> distinguishes the three registration styles (Unity Inspector,
    /// attribute, or code) which the MvcConsole displays with different icons.
    ///
    /// Readonly struct - passed by value, no allocation.
    /// </remarks>
    public readonly struct MvcLogContext
    {
        /// <summary>Source file path captured at the call site.</summary>
        public readonly string FilePath;
        /// <summary>Source line number captured at the call site.</summary>
        public readonly int LineNumber;
        /// <summary>Source member name captured at the call site.</summary>
        public readonly string MemberName;
        /// <summary>Message marker type involved in the log entry, when applicable.</summary>
        public readonly string MessageTypeName;
        /// <summary>Actor or source type involved in the log entry, when applicable.</summary>
        public readonly string SourceTypeName;
        /// <summary>Command type involved in the log entry, when applicable.</summary>
        public readonly string CommandTypeName;
        /// <summary>Service, proxy, mediator, or other target type involved in the log entry.</summary>
        public readonly string TargetTypeName;
        /// <summary>Module type associated with the log entry, when known.</summary>
        public readonly Type ModuleType;
        /// <summary>High-level category used for filtering logs.</summary>
        public readonly LogCategory Category;
        /// <summary>Composition source that produced the log entry, when applicable.</summary>
        public readonly RegistrationSource Source;
        /// <summary>Unity object associated with the log entry, used for hierarchy navigation.</summary>
        public readonly UnityEngine.Object UnityObject;

        /// <summary>
        /// Creates a structured log context.
        /// </summary>
        public MvcLogContext(
            string filePath,
            int lineNumber,
            string memberName,
            string messageTypeName = null,
            string sourceTypeName = null,
            string commandTypeName = null,
            Type moduleType = default,
            LogCategory category = LogCategory.Message,
            RegistrationSource source = RegistrationSource.Code,
            string targetTypeName = null,
            UnityEngine.Object unityObject = null)
        {
            FilePath = filePath;
            LineNumber = lineNumber;
            MemberName = memberName;
            MessageTypeName = messageTypeName;
            SourceTypeName = sourceTypeName;
            CommandTypeName = commandTypeName;
            TargetTypeName = targetTypeName;
            ModuleType = moduleType;
            Category = category;
            Source = source;
            UnityObject = unityObject;
        }

        /// <summary>
        /// High-level category used for filtering and icon selection in log consumers.
        /// </summary>
        public enum LogCategory
        {
            /// <summary>A message was published or subscribed to.</summary>
            Message,
            /// <summary>A command was bound, unbound, or executed.</summary>
            Command,
            /// <summary>A proxy was registered or accessed.</summary>
            Proxy,
            /// <summary>A mediator was attached or received a message.</summary>
            Mediator,
            /// <summary>A module lifecycle event (init started/completed, destroyed).</summary>
            Module,
            /// <summary>A service was registered or accessed.</summary>
            Service,
            /// <summary>User output via <see cref="MvcDebug"/>.</summary>
            Output
        }

        /// <summary>
        /// Identifies which of the three composition styles produced this registration event.
        /// </summary>
        public enum RegistrationSource
        {
            /// <summary>Registered programmatically inside an override method in user code.</summary>
            Code,
            /// <summary>Registered via a Unity Inspector registry component (drag-and-drop).</summary>
            Unity,
            /// <summary>Registered via attributes (<c>[Register]</c>, <c>[Bind]</c>, <c>[Attach]</c>).</summary>
            Attribute
        }
    }
}
