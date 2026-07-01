# Changelog

All notable changes to `org.mvcexpress` are documented here.

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
