# 03 - Single Module Attribute App

The same score counter built with attribute-based registration.

The module class is nearly empty. `[Register]`, `[Bind]`, and `[Attach]` attributes on the actor classes provide all composition - no Inspector wiring or override methods needed.

## What this sample shows

- `[Register]` on a `Proxy` and a `Service` class for automatic registration
- `[Bind(typeof(TMessage), typeof(TModule))]` on command classes for automatic binding
- `[Attach(FindInScene = true)]` on a mediator class for automatic scene attachment
- A `ViewPrefabCatalog` for the Canvas container prefab
- How the module class can stay almost completely empty with attribute composition

## How to open

1. Import this sample via **Window > Package Manager > mvcExpress > Samples**.
2. Open `SingleModuleApp_AttributeStyle.unity`.
3. Press Play.

The score label starts at 0. Use the +1, +5, and Reset buttons to see the flow. Module logs should show `via attribute` for command bindings and mediator attachment.
