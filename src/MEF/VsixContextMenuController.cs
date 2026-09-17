using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;

namespace VsixTreeViewer.MEF
{
    internal sealed class VsixContextMenuController : IContextMenuController
    {
        public static readonly VsixContextMenuController Instance = new();

        private VsixContextMenuController()
        {
        }

        public static VsixItemNode CurrentItem { get; private set; }

        public bool ShowContextMenu(IEnumerable<object> items, Point location)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            CurrentItem = items.OfType<VsixItemNode>().FirstOrDefault();
            if (CurrentItem == null)
            {
                return false;
            }

            int menuId = CurrentItem.IsArchiveRoot
                ? PackageIds.VsixRootContextMenu
                : CurrentItem.IsDirectory
                    ? PackageIds.VsixFolderContextMenu
                    : PackageIds.VsixFileContextMenu;

            IVsUIShell shell = VS.GetRequiredService<SVsUIShell, IVsUIShell>();
            Guid commandSet = PackageGuids.VsixTreeViewer;
            int result = shell.ShowContextMenu(
                dwCompRole: 0,
                rclsidActive: ref commandSet,
                nMenuId: menuId,
                pos: [new POINTS { x = (short)location.X, y = (short)location.Y }],
                pCmdTrgtActive: null);

            return ErrorHandler.Succeeded(result);
        }
    }
}
