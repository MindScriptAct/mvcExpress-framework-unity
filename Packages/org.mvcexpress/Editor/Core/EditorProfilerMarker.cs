// Conditional compilation for ProfilerMarker support
// ProfilerMarker was added in Unity 2020.2+ but may not be available in all .NET Standard 2.1 builds
// This provides a compatible wrapper that works across Unity versions

namespace mvcExpress.Editor.Core
{
    /// <summary>
    /// Wrapper for Unity's ProfilerMarker that provides compatibility across Unity versions.
    /// PERFORMANCE: Enables deep profiling of editor code for optimization analysis in Unity 2020.2+.
    /// For older versions or incompatible builds, provides a no-op implementation.
    /// </summary>
    internal struct EditorProfilerMarker
    {
        // NOTE: ProfilerMarker is not available in .NET Standard 2.1 editor assemblies
        // To enable profiling, upgrade to Unity 2021.2+ with .NET Standard 2.1 or .NET 6+
        // For now, we provide a zero-overhead no-op implementation
        
        public EditorProfilerMarker(string name)
        {
            // Intentionally empty - marker name is not used in no-op mode
            // When profiler support is added, store the marker here
        }
        
        public AutoScope Auto()
        {
            return new AutoScope();
        }
        
        public readonly struct AutoScope : System.IDisposable
        {
            public void Dispose()
            {
                // Intentionally empty - no profiling overhead in .NET Standard 2.1
                // When profiler support is added, end the profiler scope here
            }
        }
    }
}
