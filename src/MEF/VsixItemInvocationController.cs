using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
                    ObserveTask(OpenItemAsync(item.Info.FullName, preview));
                }
                else
                {
                    ObserveTask(item.RefreshAsync());
                }
            }

            return true;
        }

        private static async Task OpenItemAsync(string filePath, bool preview)
        {
            if (preview)
            {
                await VS.Documents.OpenInPreviewTabAsync(filePath);
                return;
            }

            await VS.Documents.OpenAsync(filePath);
        }

        private static void ObserveTask(Task task)
        {
            task.ContinueWith(t =>
            {
                if (t.Exception?.InnerException != null)
                {
                    t.Exception.InnerException.Log();
                    return;
                }

                t.Exception?.Log();
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}