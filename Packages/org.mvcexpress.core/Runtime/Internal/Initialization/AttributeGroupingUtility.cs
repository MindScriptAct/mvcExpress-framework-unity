using System;
using System.Collections.Generic;

namespace mvcExpress.Internal.Initialization
{
    /// <summary>
    /// Shared validation used when grouping stacked [Register]/[RegisterGlobal] attributes on one
    /// class into a single registration (see ModuleInitializer and MvcFacade's attribute-drain
    /// paths). Kept separate from both call sites so the conflict rule is defined once (DRY).
    /// </summary>
    internal static class AttributeGroupingUtility
    {
        /// <summary>
        /// Returns the single effective Lifecycle for a group of stacked attributes sharing one
        /// instance. Throws if any two attributes in the group specify different Lifecycle values.
        /// </summary>
        internal static RegistrationLifecycle ResolveGroupLifecycle(
            Type classType, IReadOnlyList<RegistrationLifecycle> lifecycles, string attributeName)
        {
            var first = lifecycles[0];
            for (int i = 1; i < lifecycles.Count; i++)
            {
                if (lifecycles[i] != first)
                {
                    throw new InvalidOperationException(
                        $"[{attributeName}] Type '{classType.FullName}' has stacked {attributeName} attributes " +
                        $"with conflicting Lifecycle values ('{first}' vs '{lifecycles[i]}'). All {attributeName} " +
                        $"attributes on one class that share an instance must agree on Lifecycle.");
                }
            }
            return first;
        }
    }
}
