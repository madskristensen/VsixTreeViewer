using System.Collections.Generic;
using System.Linq;

namespace VsixTreeViewer
{
    internal sealed class VsixArchiveComparison
    {
        private VsixArchiveComparison(IReadOnlyList<string> added, IReadOnlyList<string> removed, IReadOnlyList<string> changed)
        {
            Added = added;
            Removed = removed;
            Changed = changed;
        }

        public IReadOnlyList<string> Added { get; }
        public IReadOnlyList<string> Removed { get; }
        public IReadOnlyList<string> Changed { get; }
        public bool HasChanges => Added.Count > 0 || Removed.Count > 0 || Changed.Count > 0;

        public static VsixArchiveComparison Create(VsixArchive previous, VsixArchive current)
        {
            if (previous == null || current == null)
            {
                return null;
            }

            Dictionary<string, VsixArchiveEntry> previousFiles = Flatten(previous.Root);
            Dictionary<string, VsixArchiveEntry> currentFiles = Flatten(current.Root);

            string[] added = currentFiles.Keys
                .Except(previousFiles.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string[] removed = previousFiles.Keys
                .Except(currentFiles.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string[] changed = currentFiles.Keys
                .Intersect(previousFiles.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(path => HasChanged(previousFiles[path], currentFiles[path]))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new VsixArchiveComparison(added, removed, changed);
        }

        private static Dictionary<string, VsixArchiveEntry> Flatten(VsixArchiveEntry root)
        {
            var files = new Dictionary<string, VsixArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            AddFiles(root, files);
            return files;
        }

        private static void AddFiles(VsixArchiveEntry parent, IDictionary<string, VsixArchiveEntry> files)
        {
            foreach (VsixArchiveEntry child in parent.Children)
            {
                if (child.IsDirectory)
                {
                    AddFiles(child, files);
                }
                else
                {
                    files[child.FullName] = child;
                }
            }
        }

        private static bool HasChanged(VsixArchiveEntry previous, VsixArchiveEntry current)
        {
            return previous.Length != current.Length
                || previous.CompressedLength != current.CompressedLength
                || previous.LastWriteTime != current.LastWriteTime;
        }
    }
}
