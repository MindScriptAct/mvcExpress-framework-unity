# 01 - Single Module Unity App

A minimal score counter demonstrating Unity Inspector registration.

All actors (Service, Proxy, Command, Mediator) are wired through drag-drop registry components in the Unity hierarchy - no code or attributes required for composition.

## What this sample shows

- One `MvcModule` as the composition root
- `ServiceRegistryBehaviour` to register a formatting service
- `ProxyRegistryBehaviour` to register a `ProxyBehaviour` that owns score state
- `CommandBindingsBehaviour` to bind two commands to typed messages
- `MediatorRegistryBehaviour` to attach the HUD mediator to the module
- A UI flow: button click -> message -> command -> proxy state change -> message -> mediator updates label

## How to open

1. Import this sample via **Window > Package Manager > mvcExpress > Samples**.
2. Open `SingleModuleApp_UnityStyle.unity`.
3. Press Play.

The score label starts at 0. Use the +1, +5, and Reset buttons to see the message-driven data flow in action.
