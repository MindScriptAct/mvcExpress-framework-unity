# Changelog

All notable changes to `org.mvcexpress.core` are documented here.

## [0.9.3] - 2026-08-12

### Added

- Registration fluent API supports multi-mapping: `.ToLogicAs<T>()` / `.ToViewAs<T>()` (and the non-generic `Register(instance, type)` form) can now be chained multiple times with different types to register one instance under any number of logic and/or view keys in a single call - e.g. `Register(proxy).ToLogicAs<IReadOnlyThing>().ToLogicAs<IMeasurementThing>().AsPermanent()`.
- `[RegisterGlobal]` can now be stacked (`AllowMultiple = true`): apply it more than once on one class with different `LogicInterface`/`ViewInterface` values to share a single global instance across multiple resolvable types. Stacking `[Register]` (module-scoped) is now fully functional end-to-end as well - previously it collided when registering the same concrete type twice.
- `MvcFacade` code-override hooks for global registration: subclass `MvcFacade`, override `RegisterGlobalServices()` / `RegisterGlobalProxies()`, and use the new protected `GlobalDependencies` (`FacadeGlobalContainerApi`) to register or resolve against the global container before any module initializes. Runs after the Inspector-driven Global Services/Proxies registries and the `[RegisterGlobal]` attribute drain, so code registrations win on type conflicts - matching `MvcModule`'s own Inspector → Attribute → Code precedence. `MvcFacade` is no longer `sealed`, to support subclassing.
- Module extension plugin system: implement `IMvcModuleExtension` (`OnExtensionSetup`, `OnModuleInitialized`, `OnModuleDestroy`) on a component placed on an `MvcModule` GameObject or its children. `MvcModule` auto-discovers and drives it through the module's initialization/teardown lifecycle, handing it an `MvcModuleExtensionContext` with module-scoped `Messenger` and `Container` plus the app-wide `MessageBus`.
- `MvcMessageBus.SubscribeOnce<TMessage, ...>(handler)` and `MvcMessageBus.SubscribeWhen<TMessage, ...>(handler, condition)` (0-5 typed parameters): fire-once and conditional subscription helpers, previously only available through a mediator's `Messenger`, now usable directly against the message bus from proxies, services, and module extensions.

### Changed

- Calling `.ToLogicAs<T>()` / `.ToViewAs<T>()` twice for the same type within one registration chain now throws `InvalidOperationException` immediately instead of silently discarding the earlier mapping.

### Fixed

- `Container.Resolve<T>()` / `Global.Resolve<T>()` called from a mediator outside its injection window (`OnEnable`, `Update`, coroutines, event callbacks, etc.) could silently resolve against the logic container instead of the view container: scope was read from an ambient flag that only reflected "view" for the duration of `OnInitialized()`. Each actor's `Container`/`Global` now captures its scope explicitly when the actor is initialized (mediator = view, proxy = logic), independent of when the call is made.

## [0.9.2] - 2026-07-06

### Added

- Mediator prefab catalog: auto-baked catalog assets plus editor tooling for browsing and paging mediator prefabs.
- Dedicated `MessengerApi` / `MediatorMessengerApi` replacing the old messenger extension methods.

### Changed

- **Breaking:** package renamed from `org.mvcexpress` to `org.mvcexpress.core` to comply with OpenUPM's reverse-domain naming requirement (3+ segments). Update the dependency key and git-URL `?path=` references in `Packages/manifest.json` accordingly.
- **Breaking:** `AsPersistant()` renamed to `AsPermanent()` on the registration fluent API.
- **Breaking:** `AsScoped()` no longer requires the registered instance to be a `Proxy` subclass. Any plain C# type can now be scoped; only `UnityEngine.MonoBehaviour` instances are rejected, since scoped instances are constructed by the container via `Activator.CreateInstance`, which MonoBehaviours don't support.
- `[Inject]`/registration attribute handling refactored for module and global scope.
- Generator error handling and Facade/prefab-catalog editor UX improved.
- General performance improvements to the DI container and message bus hot paths.

### Fixed

- Dependency injection: services registered to both scopes no longer get `OnInitialized`/`OnCleanup` called twice; `Unregister` no longer disposes instances still resolvable under another key; `Clear`/`Dispose` no longer leak list-binding membership maps; command pool instances no longer leak across module create/destroy cycles.
- Messaging: subscription token collisions in `SubscriptionTracker`, unbounded handler-array growth under subscribe/unsubscribe churn, and transient-dependency-removal notifications silently dying after GC.
- Commands: binding a command with mismatched arity no longer silently no-ops; execution failures are no longer swallowed in production; `HasSubscribers<TMessage>` now works for zero-payload messages; `Publish` overloads report diagnostics consistently.
- `MvcModule.OnDestroy` now disposes the module's DI container.

## [0.9.1] - 2026-06-29

### Fixed

- Documentation corrected throughout: wrong XML summaries (module-scoped bus, `[BindSingleton]`, `MvcCommand`, `GlobalContainer`), stale package name (`com.msa.mvcexpress` → `org.mvcexpress`), phantom `Enabled`/`Order` fields on `MvcStartupModuleEntry`, and `CreateModule` → `SpawnModule`.
- Runtime mediator cleanup gap: `MediatorRegistrar.Cleanup()` now explicitly calls `CleanupMediator()` on runtime mediators before clearing.
- `WeakEventManager.Subscribe` made idempotent; `Unsubscribe` now uses value equality and removes all matching entries.
- `MvcMessageBus` unnecessary finalizer removed; GC finalization queue overhead eliminated.
- `BindCommandByType` reflection results now cached per `(commandType, messageType)` pair — reflection runs at most once per unique pair across all module instances.
- Pooled commands skip `[Inject]` re-injection on reuse (`_hasBeenInjected` flag); `OnInitialize()` now documented and enforced as once-per-creation.
- `[EditorBrowsable(Never)]` applied to six internal-only public types to clean up IntelliSense noise.
- Throwing `RegisterBehaviour<T>()` stub removed from `MvcDiContainer`; `IModuleBehaviourRegistrar` removed from `IModuleDiContainer` interface chain.

---

## [0.9.0] - 2026-06-20

### Initial release

- **MVC architecture** - `MvcModule`, services (plain C# classes or MonoBehaviours; no base class required), `Proxy`, `ProxyBehaviour`, `MediatorBehaviour`, `Command`, and `CommandAsync` actor types with defined roles and strict lifecycle separation
- **Dependency injection** - constructor injection, `[Inject]` property injection, and `TryResolve<T>` for optional/dynamic dependencies; no reflection in hot paths
- **Typed message bus** - single shared `MvcMessageBus` across all modules; publish/subscribe with struct or class messages; supports up to 12 typed parameters per message
- **Three registration styles** - Unity Inspector drag-drop, attribute-based (`[Register]`, `[Bind]`, `[Attach]`), and code override (`RegisterServices`, `RegisterProxies`, `BindCommands`, `AttachMediators`)
- **Strict initialization order** - Services then Proxies then Commands then Mediators then `OnInitialized`; enforced with phase validation errors in Editor and development builds
- **Async commands** - `CommandAsync` / `CommandAsync<T...>` base classes with `async`/`await` support; bindable to messages identically to synchronous commands
- **Command pooling** - configurable pool sizes via `Commander.Bind<TCmd, TMsg>(poolSize:)` or `[Bind(PoolSize = N)]` to eliminate per-execution allocations
- **Dynamic mediators** - attach and detach `MediatorBehaviour` instances at runtime via the `MediatorHub` property; mediator prefab catalog for view-type lookup
- **Automatic cleanup** - proxies, mediators, command bindings, and subscriptions are cleaned up when a module is destroyed; no manual teardown required
- **Global scope** - services and proxies registered on `MvcFacade` are injected into any module; `GlobalServiceRegistryBehaviour` and `GlobalProxyRegistryBehaviour` for Inspector configuration
- **Cross-module messaging** - all modules share one bus; any actor can subscribe to messages published by actors in other modules
- **Shared proxy mapping** - a single `Proxy` instance registered under multiple interface types so different consumers resolve the appropriate abstraction
- **`MvcFacade` singleton** - auto-created on first use (`DontDestroyOnLoad`); tracks all registered modules; supports startup module entries with ordered auto-launch and prefab instantiation
- **Module prefab support** - `MvcModule` prefabs can be registered in `MvcFacade` startup entries and instantiated with a configurable view container target
- **Logging** - per-module and global logging toggles; `MvcDebug` log routing; composition style warnings for mixed registration patterns
- **Editor tools** - code generators for modules, commands, mediators, proxies, services, messages, and view triggers; custom inspectors for all registry components
- **Samples** - three UPM sample projects covering Unity Inspector, code-first, and attribute-first registration styles
