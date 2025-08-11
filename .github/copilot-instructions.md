# Visual Studio Extension Development Guidelines

This project is a Visual Studio extension. Follow these guidelines to ensure Copilot provides effective assistance that aligns with Visual Studio extension development best practices.

## Core Principles

- **Readability First**: Many developers new to Visual Studio extensions will read this code
- **Simplicity over Cleverness**: Prefer clear, simple solutions over complex optimizations
- **Documentation**: Follow patterns from https://vsixcookbook.com

## Visual Studio Extension Best Practices

### Threading and UI Context
- Always call VS services on the UI thread when required
- Use `ThreadHelper.ThrowIfNotOnUIThread()` to validate threading requirements
- Use `await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync()` to switch to UI thread
- Handle async/await patterns properly with `JoinableTaskFactory`

### Package Lifecycle
- Use `ToolkitPackage` as base class when using Community.VisualStudio.Toolkit
- Initialize asynchronously with `InitializeAsync` override
- Handle package loading/unloading scenarios properly
- Test with VS Experimental Instance (`/rootsuffix Exp`)

### MEF Integration
- Use MEF for extending Visual Studio UI (Solution Explorer, editors, etc.)
- Apply proper MEF attributes: `[Export]`, `[Name]`, `[Order]`, `[AppliesToUIContext]`
- Follow composition patterns for loose coupling
- Handle MEF component lifecycle and disposal properly

### Error Handling
- Always handle exceptions gracefully in VS extension context
- Use `await VS.MessageBox.ShowErrorAsync()` for user-facing errors
- Log errors appropriately without blocking the UI
- Consider VS performance impact when handling errors

### Resource Management
- Implement `IDisposable` pattern for event subscriptions and resources
- Unsubscribe from events in disposal methods
- Use `using` statements for temporary resources
- Be mindful of memory leaks in long-running extensions

## Common Patterns

### Command Registration
Commands are typically defined in `.vsct` files and registered during package initialization.

### UI Context Rules
Use `ProvideUIContextRule` attributes to control when extensions load, minimizing VS startup impact.

### Service Integration
Access VS services through dependency injection or service locator patterns, always respecting threading requirements.

## Resources
- [VSIX Cookbook](https://vsixcookbook.com) - Comprehensive VS extension development guide
- [Community Toolkit](https://github.com/VsixCommunity/Community.VisualStudio.Toolkit) - Simplified VS extension development