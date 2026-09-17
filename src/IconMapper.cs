using System.IO;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell.Interop;

namespace VsixTreeViewer
{
    internal static class IconMapper
    {
        private static IVsImageService2 _imageService;

        private static IVsImageService2 GetImageService()
        {
            return _imageService ??= VS.GetRequiredService<SVsImageService, IVsImageService2>();
        }

        public static ImageMoniker GetIcon(this FileSystemInfo info, bool isOpen)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (info == null)
            {
                return KnownMonikers.Extension;
            }

            if (info is FileInfo file)
            {
                if (file.Extension.Equals(".vsix", StringComparison.OrdinalIgnoreCase))
                {
                    return KnownMonikers.Extension;
                }

                ImageMoniker moniker = GetImageService().GetImageMonikerForFile(file.FullName);

                if (moniker.Id < 0)
                {
                    moniker = KnownMonikers.Document;
                }

                return moniker;
            }

            return info.FullName.EndsWith(".vsix", StringComparison.OrdinalIgnoreCase)
                ? KnownMonikers.Extension
                : isOpen ? KnownMonikers.FolderOpened : KnownMonikers.FolderClosed;
        }

        public static ImageMoniker GetIcon(string name, bool isDirectory, bool isOpen)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (isDirectory)
            {
                return isOpen ? KnownMonikers.FolderOpened : KnownMonikers.FolderClosed;
            }

            ImageMoniker moniker = GetImageService().GetImageMonikerForFile(name);
            return moniker.Id < 0 ? KnownMonikers.Document : moniker;
        }
    }
}
