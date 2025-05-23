using System.IO;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell.Interop;

namespace VsixTreeViewer
{
    internal static class IconMapper
    {
        private static IVsImageService2 _imageService => VS.GetRequiredService<SVsImageService, IVsImageService2>();

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

                ImageMoniker moniker = _imageService.GetImageMonikerForFile(file.FullName);

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
    }
}
