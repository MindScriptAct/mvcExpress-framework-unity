using System;
using System.Collections.Generic;

namespace mvcExpress.Internal.ProxyDebug
{
    /// <summary>
    /// Tracks modules that expose proxy debug objects without keeping modules alive.
    /// </summary>
    internal static class ProxyDebugRegistry
    {
        private static readonly List<WeakReference<MvcModule>> _modules = new List<WeakReference<MvcModule>>(8);

        internal static void RegisterModule(MvcModule module)
        {
            if (module == null) return;

            // Compact stale weak references while checking for duplicate registration.
            for (int i = _modules.Count - 1; i >= 0; i--)
            {
                if (!_modules[i].TryGetTarget(out var existing) || existing == null)
                {
                    _modules.RemoveAt(i);
                    continue;
                }

                if (ReferenceEquals(existing, module))
                {
                    return;
                }
            }

            _modules.Add(new WeakReference<MvcModule>(module));
        }

        internal static void UnregisterModule(MvcModule module)
        {
            if (module == null) return;

            // Remove both the requested module and any dead weak references.
            for (int i = _modules.Count - 1; i >= 0; i--)
            {
                if (!_modules[i].TryGetTarget(out var existing) || existing == null || ReferenceEquals(existing, module))
                {
                    _modules.RemoveAt(i);
                }
            }
        }
    }
}
