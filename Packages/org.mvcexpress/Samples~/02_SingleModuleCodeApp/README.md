# 02 - Single Module Code App

The same score counter as Sample 01, rebuilt using code-first registration.

All wiring is done in `SingleModuleCodeAppModule.cs` - no registry components or attributes. This style gives you full control and is easy to trace in code.

## What this sample shows

- `Container.Register(...)` for services and proxies from code
- `.ToLogic()` / `.ToViewAs<IInterface>()` to scope registrations
- `Commander.Bind<TCmd, TMsg>()` to bind commands from code
- `MediatorHub.AttachPrefab<T>()` to attach a prefab-based mediator from code
- A `ViewPrefabCatalog` mapping mediator types to prefabs
- Shared proxy mapping: commands inject the concrete type, mediators inject a read-only interface

## How to open

1. Import this sample via **Window > Package Manager > mvcExpress > Samples**.
2. Open `SingleModuleApp_CodeStyle.unity`.
3. Press Play.

The score label starts at 0. Use the +1, +5, and Reset buttons to see the flow. Module logs should show `by code` for command bindings and mediator attachment.
