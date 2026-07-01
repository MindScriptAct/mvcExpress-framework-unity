# mvcExpress

![Version](https://img.shields.io/badge/version-0.9.1-blue) ![Unity](https://img.shields.io/badge/unity-2021.3%2B-black) ![License](https://img.shields.io/badge/license-MIT-green)

**A lightweight, type-safe MVC framework for Unity, built for performance and team scalability.**

## Why mvcExpress exists

Unity projects tend to start clean and end up as a tangle of MonoBehaviours that read input, hold state, call APIs, and update the UI all in the same script. That's fine for a prototype, but it gets expensive as a team and a codebase grow: business logic is hard to test without a running scene, a change in one script silently ripples into others, and onboarding a new programmer means reading spaghetti to find out "where does X actually happen."

mvcExpress imposes a small, strict set of actor roles (Service, Proxy, Command, Mediator) that only communicate through typed messages and a dependency injection container - never through direct references to each other. The C# type system, not a wiki page, is what enforces the architecture: every actor is registered, resolved, and dispatched by its class type, so mistakes tend to surface as compile errors instead of runtime mysteries. Business logic (Commands, Proxies, Services) is plain C#, so it can be unit tested without a Unity scene or Play Mode.

The framework doesn't dictate everything beyond the four roles: it recommends good patterns through docs, editor warnings, and tooling, but doesn't force a single workflow. Pick whichever of the three registration styles (Inspector drag-drop, attributes, code) fits your team, and mix them per module.

## Architecture at a glance

```
MvcFacade (singleton, DontDestroyOnLoad)
 +- Global DI container + global message bus, shared by all modules
 |
 +-- MvcModule           composition root for one scene / subsystem / subApp
      +-- Services       stateless helpers, calculations, SDK adapters, utils
      +-- Proxies        state, data, persistence -- publish messages, never subscribe
      +-- Commands       business logic triggered by messages -- the only actors that should write to Proxies
      +-- Mediators      MonoBehaviour bridge to Unity views -- can subscribe/publish messages for communication
```

View input becomes a published message. A bound Command reads/writes a Proxy and calls Services. The Proxy publishes a state-change message. Subscribed Mediators update the view. No actor holds a direct reference to another actor.

## Features

- **MVC actors** - Service/ServiceBehaviour, Proxy/ProxyBehaviour, Command/CommandAsync, MediatorBehaviour, each with one job and an enforced dependency direction
- **Type-safe dependency injection** - constructor and `[Inject]` property injection, module-scoped and global containers, `TryResolve<T>` for optional lookups, no reflection in hot paths
- **Typed message bus** - `IMessage` / `IMessage<T1..T12>`; prefer `readonly struct` for zero-GC publish; one bus shared across all modules
- **Async commands** - `CommandAsync<T...>` with `async`/`await`, bound identically to sync commands
- **Command pooling** - fixed-size pools remove per-dispatch allocation for high-frequency messages
- **Three registration styles, mixable per module** - Unity Inspector components, `[Register]`/`[Bind]`/`[Attach]` attributes, or code overrides
- **Strict initialization order** - Services -> Proxies -> Commands -> Mediators -> `OnInitialized()`, validated in Editor and development builds
- **Dynamic mediators** - attach/detach `MediatorBehaviour` instances at runtime via `MediatorHub`, with a prefab catalog for view lookup
- **Global scope** - promote a service or proxy to `MvcFacade` so any module can inject it
- **Shared proxy mapping** - register one proxy instance under a write interface for Commands and a read-only interface for Mediators
- **Automatic cleanup** - subscriptions, bindings, and actor teardown are handled when a module is destroyed
- **Editor tooling** - code generators for every actor type, custom inspectors, composition-style enforcement (soft warning or hard compile error)
- **`MvcDebug` logging** - zero-allocation, compiled out of release builds unless explicitly enabled [Work in progress...]

## Benefits

- Business logic lives in plain C# (Commands, Proxies, Services), so it can be tested without a Unity scene or Play Mode
- No reflection in the dispatch/injection hot paths and no per-frame allocation from struct messages - friendly to IL2CPP/AOT builds and mobile performance budgets
- Compile-time wiring: renaming or deleting a type breaks the build, not a runtime lookup
- Scales from a single-scene prototype (one module, Inspector wiring) to a multi-module app (global proxies, runtime module spawning) without switching tools
- Read-only view interfaces make "who is allowed to write this state" a type-checked question, not a code-review reminder
- Zero third-party dependencies

## Trade-offs

- More ceremony than a raw MonoBehaviour for a one-off script: a simple action is a Message plus a Command plus a binding, not just a method call
- The message bus is application-wide with no per-module filtering;
- Async commands have no built-in `CancellationToken` - cancellation is on you, typically via a Proxy or Service holding a `CancellationTokenSource`
- Pre-1.0 (`0.9.x`): the public API is close to settled but may still shift before `1.0`, and some tooling (composition-style hard enforcement, the debugging suite) is still in progress
- Opinionated by design: teams that want unrestricted freedom in where logic lives may feel the actor boundaries as friction

## Requirements

- Unity 2021.3+
- .NET Standard 2.1, C# 9
- No third-party dependencies

## Installation

Add via **Window > Package Manager > Add package from git URL**:

```
https://github.com/MindScriptAct/mvcExpress-framework-unity.git?path=Packages/org.mvcexpress
```

Or add directly to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "org.mvcexpress": "https://github.com/MindScriptAct/mvcExpress-framework-unity.git?path=Packages/org.mvcexpress"
  }
}
```

Pin to a specific version by appending a tag:

```
https://github.com/MindScriptAct/mvcExpress-framework-unity.git?path=Packages/org.mvcexpress#0.9.1
```

## Quick example

```csharp
// Message: zero-allocation struct payload
public readonly struct AddGoldMessage : IMessage<int> { }
public readonly struct GoldChangedMessage : IMessage<int> { }

// Proxy: owns state, publishes when it changes
public class WalletProxy : Proxy
{
    public int Gold { get; private set; }
    public void Add(int amount)
    {
        Gold += amount;
        Messenger.Publish<GoldChangedMessage, int>(Gold);
    }
}

// Command: the only actor that writes to WalletProxy, bound to AddGoldMessage
public class AddGoldCommand : Command<int>
{
    [Inject] private WalletProxy _wallet;
    public override void Execute(int amount) => _wallet.Add(amount);
}

// Module: wires it together
public class GameplayModule : MvcModule
{
    protected override void RegisterProxies() =>
        Container.Register(new WalletProxy()).ToLogic().AsPersistent();

    protected override void BindCommands() =>
        Commander.Bind<AddGoldCommand, AddGoldMessage, int>();
}

// Mediator: reacts to the state change, no business logic
public class WalletMediator : MediatorBehaviour
{
    [SerializeField] private TMP_Text _label;
    protected override void OnInitialized() =>
        Messenger.Subscribe<GoldChangedMessage, int>(gold => _label.text = gold.ToString());
}
```

## Samples

Three importable sample projects (**Window > Package Manager > mvcExpress > Samples**) build the same score-counter app with a different registration style, so you can compare them directly:

| Sample | Registration style |
|---|---|
| 01 - Single Module Unity App | Unity Inspector drag-drop |
| 02 - Single Module Code App | Code-first |
| 03 - Single Module Attribute App | Attribute-based |

## Documentation

Full docs, guides, and API reference: **[mvcexpress.org](https://www.mvcexpress.org/)**

## Roadmap

- **Multi-module sample** demonstrating cross-module messaging, global proxies, and runtime module spawning
- **Debugging suite** - message bus console, actor timelines, dependency graphs, live application state overview
- **mvcExpress Live** - an extension package for scheduled updates, timers, and reactive data streams

## License

MIT - see [LICENSE.md](LICENSE.md)
