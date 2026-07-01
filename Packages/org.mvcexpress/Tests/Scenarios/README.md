# Scenario Tests

Play Mode integration tests that verify complete framework scenarios end-to-end.

These tests are in a separate assembly (`org.mvcexpress.Tests.Scenarios`) so they can be
toggled independently in the Unity Test Runner - run unit tests fast, scenario tests separately.

Scenario categories are implemented and active. See TESTING_TODO.md for the original implementation priority and coverage notes.

## Categories

| Folder | What it tests |
|--------|--------------|
| Initialization/ | Module startup, registration paths, initialization order |
| Messaging/ | Command/proxy publish-subscribe flows, struct messages, mediator subscriptions |
| CommandChains/ | Command dispatch, chained commands, async commands |
| TransientLifecycle/ | Transient proxy lifecycle, pool invalidation |
| GlobalScope/ | Global services, cross-module injection |
| MultiModule/ | Multi-module isolation, inter-module communication |
| EdgeCases/ | Error handling, container exceptions, edge cases |



