using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Internal.VisualStudio.PlatformUI;

namespace VsixTreeViewer.MEF
{
    internal class VsixItemInvocationController : IInvocationController
    {
        // Singleton instance to avoid creating new instances for each node
        public static readonly VsixItemInvocationController Instance = new();

        private VsixItemInvocationController()
        {
            // Private constructor for singleton
        }

        public bool Invoke(IEnumerable<object> items, InputSource inputSource, bool preview)
        {
            foreach (VsixItemNode item in items.OfType<VsixItemNode>())
            {
                if (item.Info is FileInfo)
                {
                    if (preview)
                    {
                        VS.Documents.OpenInPreviewTabAsync(item.Info.FullName).FireAndForget();
                    }
                    else
                    {
                        VS.Documents.OpenAsync(item.Info.FullName).FireAndForget();
                    }
                }
                else
                {
                    item.RefreshAsync().FireAndForget();
                }
            }

            return true;
        }
    }
}