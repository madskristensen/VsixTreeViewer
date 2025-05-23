global using System;
global using Community.VisualStudio.Toolkit;
global using Microsoft.VisualStudio.Shell;
global using Task = System.Threading.Tasks.Task;
using System.Runtime.InteropServices;
using System.Threading;

namespace VsixTreeViewer
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageGuids.VsixTreeViewerString)]
    [ProvideUIContextRule(PackageGuids.HasVsixProjectString,
    name: "autoload",
    expression: "vsix",
    termNames: ["vsix"],
    termValues: ["SolutionHasProjectFlavor:" + ProjectTypes.EXTENSIBILITY])]
    public sealed class VsixTreeViewerPackage : ToolkitPackage
    {
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.RegisterCommandsAsync();
        }
    }
}