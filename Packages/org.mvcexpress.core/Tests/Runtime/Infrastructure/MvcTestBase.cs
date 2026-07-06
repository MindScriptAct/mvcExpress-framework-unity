using System;

namespace mvcExpress.Tests
{
    // Base class for test fixtures that need shared test utilities.
    // Does not define [SetUp]/[TearDown] - those stay in each test class.
    public class MvcTestBase
    {
        protected static class GcHelper
        {
            // Three-call sequence required for deterministic weak-reference tests:
            // first pass promotes unreachable objects into the finalization queue,
            // WaitForPendingFinalizers drains it, second pass collects what remains.
            public static void ForceCollect()
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }
    }
}
