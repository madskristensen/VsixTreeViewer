using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Internal.VisualStudio.PlatformUI;

namespace VsixTreeViewer.MEF
{
    internal class VsixItemInvocationController : IInvocationController
    {
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
                    SendKeys.Send("{RIGHT}");
                }
            }

            return true;
        }
    }
}