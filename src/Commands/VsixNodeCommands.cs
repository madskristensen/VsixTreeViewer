using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.VisualStudio.Threading;
using VsixTreeViewer.MEF;
using Forms = System.Windows.Forms;

namespace VsixTreeViewer.Commands
{
    internal static class VsixCommandHelpers
    {
        public static VsixItemNode CurrentItem => VsixContextMenuController.CurrentItem;

        public static async System.Threading.Tasks.Task<string> MaterializeCurrentFileAsync()
        {
            VsixItemNode item = CurrentItem;
            if (item == null || item.IsDirectory)
            {
                return null;
            }

            await System.Threading.Tasks.TaskScheduler.Default;
            return item.GetOpenPath();
        }

        public static void SelectInExplorer(string path)
        {
            if (File.Exists(path))
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
        }
    }

    [Command(PackageIds.OpenInFileExplorer)]
    internal sealed class OpenInFileExplorerCommand : BaseCommand<OpenInFileExplorerCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            string path = VsixCommandHelpers.CurrentItem?.RootNode?.VsixPath;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            VsixCommandHelpers.SelectInExplorer(path);
        }
    }

    [Command(PackageIds.OpenContainingFolder)]
    internal sealed class OpenContainingFolderCommand : BaseCommand<OpenContainingFolderCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            string path = await VsixCommandHelpers.MaterializeCurrentFileAsync();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            VsixCommandHelpers.SelectInExplorer(path);
        }
    }

    [Command(PackageIds.CopyPath)]
    internal sealed class CopyPathCommand : BaseCommand<CopyPathCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            string path = VsixCommandHelpers.CurrentItem?.PackagePath;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (!string.IsNullOrWhiteSpace(path))
            {
                Clipboard.SetText(path);
            }
        }
    }

    [Command(PackageIds.CopyFile)]
    internal sealed class CopyFileCommand : BaseCommand<CopyFileCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            string path = await VsixCommandHelpers.MaterializeCurrentFileAsync();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (File.Exists(path))
            {
                var files = new StringCollection { path };
                Clipboard.SetFileDropList(files);
            }
        }
    }

    [Command(PackageIds.ExtractVsix)]
    internal sealed class ExtractVsixCommand : BaseCommand<ExtractVsixCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            VsixArchive archive = VsixCommandHelpers.CurrentItem?.Archive;
            if (archive == null)
            {
                return;
            }

            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = "Select a folder for the extracted VSIX contents",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() != Forms.DialogResult.OK)
            {
                return;
            }

            string targetDirectory = dialog.SelectedPath;
            await System.Threading.Tasks.TaskScheduler.Default;
            archive.ExtractToDirectory(targetDirectory);
            Process.Start("explorer.exe", $"\"{targetDirectory}\"");
        }
    }

    [Command(PackageIds.OpenManifest)]
    internal sealed class OpenManifestCommand : BaseCommand<OpenManifestCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            VsixArchive archive = VsixCommandHelpers.CurrentItem?.Archive;
            VsixArchiveEntry manifest = archive?.FindManifestEntry();
            if (manifest == null)
            {
                return;
            }

            await System.Threading.Tasks.TaskScheduler.Default;
            string path = archive.Materialize(manifest);
            await VS.Documents.OpenAsync(path);
        }
    }

    [Command(PackageIds.RebuildVsixProject)]
    internal sealed class RebuildVsixProjectCommand : BaseCommand<RebuildVsixProjectCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            VsixCommandHelpers.CurrentItem?.RootNode?.RebuildProject();
        }
    }

    [Command(PackageIds.RefreshVsix)]
    internal sealed class RefreshVsixCommand : BaseCommand<RefreshVsixCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            VsixCommandHelpers.CurrentItem?.RootNode?.Refresh();
        }
    }
}
