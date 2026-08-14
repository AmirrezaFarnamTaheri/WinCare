# Source-Generated Command Dispatcher — Zero-Reflection C# Dispatch Engine

## Problem Statement
How might we eliminate runtime reflection and dictionary lookups during WinCare command execution to achieve sub-millisecond cold start times and zero heap allocations during high-frequency maintenance dispatches?

## Recommended Direction
Build a custom C# Source Generator (`WinCare.SourceGenerators`) that inspects command handler implementations decorated with `[CommandHandler("command.id")]` at compile time and emits a strongly-typed, zero-reflection dispatch routing table.

Instead of scanning assemblies via `Assembly.GetTypes()` or constructing `IServiceProvider` reflection scopes at startup, the generated code produces a static `switch` expression mapping command IDs directly to handler execution delegates.

## Key Assumptions to Validate
- [ ] **Startup Speed:** Cold start application initialization time drops by >40ms on low-spec hardware.
- [ ] **AOT Compatibility:** Source-generated dispatcher builds cleanly with Native AOT compilation (`PublishAot=true`).
- [ ] **Compilation Overhead:** Roslyn Source Generator adds <500ms to total build time.

## MVP Scope
### What's In
- `[CommandHandler]` attribute definition in `WinCare.Domain`.
- C# Source Generator project (`src/WinCare.SourceGenerators/CommandDispatcherGenerator.cs`).
- Generated static `GeneratedCommandDispatcher.g.cs` partial class.

### What's Out
- Source generation for third-party dynamic plugins (plugins use `JsonPluginLoader` / `AssemblyPluginLoader`).

## Not Doing (and Why)
- **Runtime Expression Trees (`Expression.Compile`):** Replaced with compile-time source generation to support Native AOT trim safety.
- **Heavy Reflection DI Containers:** Avoided to maintain instant startup performance.

## Open Questions
- Can Roslyn Source Generators automatically validate that every command handler implements cancellation token propagation?
