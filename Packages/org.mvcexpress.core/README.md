# mvcExpress

Lightweight MVC framework for Unity (2021.3+) with modular architecture, dependency injection, and a typed message bus.

Full documentation and guides: **[mvcexpress.org](https://www.mvcexpress.org/)**

## Messaging invariant

One application has exactly one `MessageBus`. This is a fundamental mvcExpress design choice, not a configurable scope: `MvcFacade` owns the bus and every module and actor uses it. Every publish is application-wide. mvcExpress does not provide module-local buses, target-module buses, message bridges, or per-module delivery filters.

## Features

- **Modular architecture** - each `MvcModule` is an isolated composition root
- **Dependency injection** - constructor and `[Inject]` property injection; no reflection in hot paths
- **Typed message bus** - zero-allocation struct messages on the single application-wide bus shared by every module
- **Three registration styles** - Unity Inspector drag-drop, `[Register]`/`[Bind]`/`[Attach]` attributes, or code
- **Sync and async commands** - `Command` and `CommandAsync`, both bindable to typed messages
- **Dynamic mediators** - attach and detach `MediatorBehaviour` instances at runtime
- **Global scope** - services and proxies on `MvcFacade` are visible to all modules
- **Automatic cleanup** - subscriptions, bindings, and actors are torn down on module destroy

## Installation

Add via Unity Package Manager using the git URL:

```
https://github.com/MindScriptAct/mvcExpress-framework-unity.git?path=Packages/org.mvcexpress.core
```

Or add directly to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "org.mvcexpress.core": "https://github.com/MindScriptAct/mvcExpress-framework-unity.git?path=Packages/org.mvcexpress.core"
  }
}
```

## Samples

Three sample projects are included and importable from **Window > Package Manager > mvcExpress > Samples**:

- **01 - Single Module Unity App** - Inspector drag-drop registration
- **02 - Single Module Code App** - code-first registration
- **03 - Single Module Attribute App** - attribute-based registration

## Documentation

[mvcexpress.org](https://www.mvcexpress.org/)
