using System;
using System.Collections.Generic;
using System.Text;

namespace mvcExpress
{
    /// <summary>
    /// Thrown when one or more attribute-driven composition registrations
    /// (<c>[Register]</c>/<c>[Bind]</c>/<c>[Attach]</c>/<c>[RegisterGlobal]</c>) fail validation.
    /// </summary>
    /// <remarks>
    /// Initialization deliberately runs to completion - every registration is attempted, and every
    /// failure is collected - instead of aborting on the first one. This exception is thrown once,
    /// after the full pass finishes, so a developer sees every configuration problem from a single
    /// run instead of fixing one, re-running, and finding the next.
    ///
    /// Unlike <c>MvcDebug.LogError</c>, throwing this exception is never conditionally compiled out -
    /// invalid attribute composition is a configuration bug and must stop the affected module/facade
    /// in every build configuration, including a clean release player.
    /// </remarks>
    public sealed class MvcCompositionException : Exception
    {
        /// <summary>
        /// Every individual failure message collected during the initialization pass, in the order
        /// they were discovered.
        /// </summary>
        public IReadOnlyList<string> Failures { get; }

        /// <summary>
        /// Builds the exception from the collected failure messages for one scope (a module name or
        /// "global registration").
        /// </summary>
        /// <param name="scopeName">Module name, or "global registration" for a [RegisterGlobal] failure.</param>
        /// <param name="failures">Every failure message collected during the pass; never empty when thrown.</param>
        public MvcCompositionException(string scopeName, IReadOnlyList<string> failures)
            : base(BuildMessage(scopeName, failures))
        {
            Failures = failures ?? Array.Empty<string>();
        }

        private static string BuildMessage(string scopeName, IReadOnlyList<string> failures)
        {
            var count = failures?.Count ?? 0;

            var sb = new StringBuilder();
            sb.Append("[MvcExpress] ").Append(count).Append(count == 1 ? " attribute composition error" : " attribute composition errors")
              .Append(" in '").Append(scopeName).Append("':");

            if (failures != null)
            {
                for (int i = 0; i < failures.Count; i++)
                {
                    sb.Append('\n').Append(i + 1).Append(") ").Append(failures[i]);
                }
            }

            return sb.ToString();
        }
    }
}
