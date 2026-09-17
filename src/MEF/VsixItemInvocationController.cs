using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Threading;

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
                if (!item.IsDirectory)
                {
                    ObserveTask(OpenItemAsync(item, preview));
                }
                else
                {
                    ObserveTask(item.RefreshAsync());
                }
            }

            return true;
        }

        private static async Task OpenItemAsync(VsixItemNode item, bool preview)
        {
            await TaskScheduler.Default;
            string filePath = item.GetOpenPath();

            if (!string.IsNullOrWhiteSpace(filePath))
            {
                await OpenItemAsync(filePath, preview);
            }
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
            _ = task.ContinueWith(t =>
            {
                if (t.Exception?.InnerException != null)
                {
                    t.Exception.InnerException.Log();
                    return;
                }

                t.Exception?.Log();
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }
}