# Copilot Instructions for VsixTreeViewer

## Project Overview
This is a Visual Studio extension project that provides a tree viewer for VSIX files in Solution Explorer. The extension shows the content of compiled .vsix files directly in Solution Explorer, making it easy to inspect manifests and other files.

## Technology Stack
- **Framework**: .NET Framework 4.8
- **Language**: Latest C# version supported on .NET Framework 4.8 (C# 7.3)
- **Extension Framework**: VSIX Community Toolkit (Community.VisualStudio.Toolkit.17)
- **Build System**: MSBuild with VSSDK build tools
- **Documentation**: Follows patterns from https://vsixcookbook.com

## Project Structure
```
/src
├── VsixTreeViewer.csproj          # Main project file
├── source.extension.vsixmanifest  # VSIX manifest defining extension metadata
├── VsixTreeViewerPackage.cs       # Main package class (entry point)
├── VSCommandTable.vsct            # Visual Studio command table
├── /MEF                           # MEF components for Solution Explorer integration
│   ├── VsixItemSourceProvider.cs  # Provides tree items for VSIX files
│   ├── VsixRootNode.cs            # Root node for VSIX tree
│   ├── VsixItemNode.cs            # Individual item nodes
│   └── VsixItemInvocationController.cs # Handles item interactions
├── IconMapper.cs                  # Maps file types to icons
├── Debouncer.cs                   # Utility for debouncing operations
└── Relationships.cs               # Defines item relationships
```

## Key Architecture Patterns

### VSIX Community Toolkit Usage
- Use `ToolkitPackage` as base class for main package
- Follow async initialization patterns with `InitializeAsync`
- Use toolkit's service registration and command handling
- Leverage toolkit's MEF integration for Solution Explorer extensibility

### MEF (Managed Extensibility Framework)
- Use MEF for extending Solution Explorer with custom nodes
- Implement `IFileSystemWatcher` patterns for file monitoring
- Follow composition patterns for loose coupling

### Visual Studio SDK Integration
- Package registration with proper attributes (`PackageRegistration`, `InstalledProductRegistration`)
- UI context rules for proper loading (`ProvideUIContextRule`)
- Menu and command integration via VSCT files

## Development Guidelines

### Code Style and Maintainability
- **Readability First**: A lot of developers with no experience in Visual Studio extensions will be reading this code
- **Simplicity is Key**: Prefer simple, clear solutions over complex optimizations
- **Async/Await**: Use async patterns consistently, especially for VS services
- **Error Handling**: Always handle exceptions gracefully in VS extension context
- **Disposable Pattern**: Properly dispose of resources and event subscriptions

### Visual Studio Extension Best Practices
- Always call VS services on the UI thread when required
- Use `ThreadHelper.ThrowIfNotOnUIThread()` to validate threading
- Handle package loading/unloading scenarios properly
- Test with both experimental instance and real VS installation
- Follow VS theming and accessibility guidelines

### File Naming and Organization
- Use PascalCase for all C# files and classes
- Group related functionality in folders (e.g., `/MEF` for MEF components)
- Keep the main package class simple and delegate to specialized classes
- Use descriptive names that clearly indicate the component's purpose

## Build and Deployment

### Build Process
- Use MSBuild with VSSDK build tools
- Build configuration: Debug/Release
- Output: `.vsix` file in `bin/{Configuration}/` directory
- The build process generates source.extension.cs from the manifest

### Dependencies
- Community.VisualStudio.Toolkit.17 (main framework)
- Microsoft.VisualStudio.SDK (VS SDK)
- Microsoft.VSSDK.BuildTools (build-time tools)
- Community.VisualStudio.VSCT (command table compilation)

### Testing
- Test in VS Experimental Instance (`/rootsuffix Exp`)
- Verify extension loads properly and doesn't affect VS performance
- Test with various project types and VSIX files
- Verify proper cleanup when extension is disabled/uninstalled

## Common Tasks and Patterns

### Adding New Commands
1. Define command in `VSCommandTable.vsct`
2. Generate command IDs in `VSCommandTable.cs`
3. Register command handler in package initialization
4. Implement command logic with proper error handling

### Extending Solution Explorer
1. Create MEF components in `/MEF` folder
2. Implement appropriate interfaces (`IFileSystemItemSource`, etc.)
3. Handle file system events and updates
4. Provide appropriate icons and context menus

### Working with VSIX Files
- Use System.IO.Compression for reading ZIP-based VSIX files
- Parse manifest XML carefully with proper error handling
- Cache results appropriately to avoid repeated file system access
- Handle file locking issues when VSIX is being built

## Code Examples and Patterns

### MEF Component Pattern
```csharp
[Export(typeof(IAttachedCollectionSourceProvider))]
[Name(nameof(YourProvider))]
[Order(Before = HierarchyItemsProviderNames.Contains)]
[AppliesToUIContext(PackageGuids.HasVsixProjectString)]
internal class YourProvider : IAttachedCollectionSourceProvider
{
    // Implementation
}
```

### Package Initialization Pattern
```csharp
public sealed class YourPackage : ToolkitPackage
{
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await this.RegisterCommandsAsync();
        // Additional initialization
    }
}
```

### Thread Safety Pattern
```csharp
public async Task SomeVSOperation()
{
    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
    ThreadHelper.ThrowIfNotOnUIThread();
    // VS service calls here
}
```

### Error Handling Pattern
```csharp
try
{
    // VS extension operations
}
catch (Exception ex)
{
    // Log error appropriately
    await VS.MessageBox.ShowErrorAsync("Operation failed", ex.Message);
}
```

## Project-Specific Considerations

### VSIX File Structure
- VSIX files are ZIP archives with specific manifest structure
- Main manifest is `extension.vsixmanifest` in the root
- Content is organized by type (assemblies, assets, etc.)
- This extension reads and displays this structure in Solution Explorer

### UI Context and Loading
- Extension loads only when a VSIX project is present (`HasVsixProject` context)
- Uses `SolutionHasProjectFlavor` with `EXTENSIBILITY` project type
- Avoids unnecessary loading to maintain VS performance

### File System Monitoring
- Monitor VSIX file changes during development/build
- Use debouncing to avoid excessive updates during rapid changes
- Handle cases where VSIX file is locked during build process

### Icon and Display Management
- Map file extensions to appropriate VS icons using `IconMapper`
- Handle unknown file types gracefully
- Maintain consistency with VS Solution Explorer theming

## Debugging and Troubleshooting
- Enable diagnostic logging in VS experimental instance
- Use VS debugger attached to experimental instance
- Check VS Activity Log for extension loading issues
- Verify MEF composition in VS diagnostics

## Resources
- [VSIX Cookbook](https://vsixcookbook.com) - Primary documentation
- [Community Toolkit GitHub](https://github.com/VsixCommunity/Community.VisualStudio.Toolkit)
- [Visual Studio SDK Documentation](https://docs.microsoft.com/en-us/visualstudio/extensibility/)

## Team Best Practices
- Code must be readable and maintainable, especially for new team members
- Prefer explicit over implicit when it improves clarity
- Document any complex VS SDK interactions with comments
- Always test changes in both Debug and Release configurations
- Consider backward compatibility when making changes to public APIs