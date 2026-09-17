using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace VsixTreeViewer
{
    internal sealed class VsixArchive
    {
        private VsixArchive(string snapshotPath, VsixArchiveEntry root, string manifestContent)
        {
            SnapshotPath = snapshotPath;
            Root = root;
            Root.SetOwner(this);
            ManifestContent = manifestContent;
        }

        public string SnapshotPath { get; }
        public VsixArchiveEntry Root { get; }
        public string ManifestContent { get; }

        public VsixArchiveEntry FindManifestEntry()
        {
            return FindEntry(Root, entry => !entry.IsDirectory && entry.Name.EndsWith(".vsixmanifest", StringComparison.OrdinalIgnoreCase));
        }

        public static VsixArchive Load(string snapshotPath)
        {
            var root = new MutableEntry(string.Empty, string.Empty, isDirectory: true, length: 0, DateTimeOffset.MinValue);
            string manifestContent = null;

            using (ZipArchive archive = ZipFile.OpenRead(snapshotPath))
            {
                foreach (ZipArchiveEntry zipEntry in archive.Entries)
                {
                    string[] segments = GetSafeSegments(zipEntry.FullName);
                    if (segments.Length == 0)
                    {
                        continue;
                    }

                    MutableEntry parent = root;
                    for (int index = 0; index < segments.Length - 1; index++)
                    {
                        parent = parent.GetOrAddDirectory(segments[index]);
                    }

                    bool isDirectory = zipEntry.FullName.EndsWith("/", StringComparison.Ordinal)
                        || zipEntry.FullName.EndsWith("\\", StringComparison.Ordinal);
                    MutableEntry entry = isDirectory
                        ? parent.GetOrAddDirectory(segments[segments.Length - 1])
                        : parent.AddFile(segments[segments.Length - 1], zipEntry.FullName.Replace('\\', '/'), zipEntry.Length, zipEntry.LastWriteTime);

                    if (!isDirectory && manifestContent == null && entry.Name.EndsWith(".vsixmanifest", StringComparison.OrdinalIgnoreCase))
                    {
                        using StreamReader reader = new(zipEntry.Open());
                        manifestContent = reader.ReadToEnd();
                    }
                }
            }

            return new VsixArchive(snapshotPath, root.Freeze(owner: null), manifestContent);
        }

        public string Materialize(VsixArchiveEntry entry)
        {
            if (entry == null || entry.IsDirectory)
            {
                return null;
            }

            string rootDirectory = VsixTemporaryFiles.GetMaterializedRoot(SnapshotPath);
            string targetPath = Path.GetFullPath(Path.Combine(rootDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            string normalizedRoot = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!targetPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"The VSIX entry path '{entry.FullName}' is invalid.");
            }

            if (File.Exists(targetPath) && new FileInfo(targetPath).Length == entry.Length)
            {
                return targetPath;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

            using ZipArchive archive = ZipFile.OpenRead(SnapshotPath);
            ZipArchiveEntry zipEntry = archive.GetEntry(entry.FullName)
                ?? archive.Entries.FirstOrDefault(candidate => string.Equals(candidate.FullName.Replace('\\', '/'), entry.FullName, StringComparison.OrdinalIgnoreCase));

            if (zipEntry == null)
            {
                throw new InvalidDataException($"The VSIX entry '{entry.FullName}' no longer exists.");
            }

            zipEntry.ExtractToFile(targetPath, overwrite: true);
            VsixTemporaryFiles.TouchMaterializedRoot(rootDirectory);
            return targetPath;
        }

        public void ExtractToDirectory(string targetDirectory)
        {
            foreach (VsixArchiveEntry entry in EnumerateFiles(Root))
            {
                string sourcePath = Materialize(entry);
                string targetPath = Path.GetFullPath(Path.Combine(targetDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                string normalizedTarget = Path.GetFullPath(targetDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

                if (!targetPath.StartsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"The VSIX entry path '{entry.FullName}' is invalid.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                File.Copy(sourcePath, targetPath, overwrite: true);
            }
        }

        private static VsixArchiveEntry FindEntry(VsixArchiveEntry parent, Func<VsixArchiveEntry, bool> predicate)
        {
            foreach (VsixArchiveEntry child in parent.Children)
            {
                if (predicate(child))
                {
                    return child;
                }

                VsixArchiveEntry match = FindEntry(child, predicate);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static IEnumerable<VsixArchiveEntry> EnumerateFiles(VsixArchiveEntry parent)
        {
            foreach (VsixArchiveEntry child in parent.Children)
            {
                if (child.IsDirectory)
                {
                    foreach (VsixArchiveEntry descendant in EnumerateFiles(child))
                    {
                        yield return descendant;
                    }
                }
                else
                {
                    yield return child;
                }
            }
        }

        private static string[] GetSafeSegments(string entryPath)
        {
            string[] segments = (entryPath ?? string.Empty)
                .Replace('\\', '/')
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            return segments.Any(segment => segment == "." || segment == ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                ? Array.Empty<string>()
                : segments;
        }

        private sealed class MutableEntry
        {
            private readonly Dictionary<string, MutableEntry> _children = new(StringComparer.OrdinalIgnoreCase);

            public MutableEntry(string name, string fullName, bool isDirectory, long length, DateTimeOffset lastWriteTime)
            {
                Name = name;
                FullName = fullName;
                IsDirectory = isDirectory;
                Length = length;
                LastWriteTime = lastWriteTime;
            }

            public string Name { get; }
            public string FullName { get; }
            public bool IsDirectory { get; }
            public long Length { get; }
            public DateTimeOffset LastWriteTime { get; }

            public MutableEntry GetOrAddDirectory(string name)
            {
                if (_children.TryGetValue(name, out MutableEntry existing))
                {
                    return existing;
                }

                string fullName = string.IsNullOrEmpty(FullName) ? name : FullName + "/" + name;
                var directory = new MutableEntry(name, fullName, isDirectory: true, length: 0, DateTimeOffset.MinValue);
                _children.Add(name, directory);
                return directory;
            }

            public MutableEntry AddFile(string name, string fullName, long length, DateTimeOffset lastWriteTime)
            {
                var file = new MutableEntry(name, fullName, isDirectory: false, length, lastWriteTime);
                _children[name] = file;
                return file;
            }

            public VsixArchiveEntry Freeze(VsixArchive owner)
            {
                var entry = new VsixArchiveEntry(owner, Name, FullName, IsDirectory, Length, LastWriteTime);
                entry.SetChildren(_children.Values.Select(child => child.Freeze(owner)).ToArray());
                return entry;
            }
        }
    }

    internal sealed class VsixArchiveEntry
    {
        private IReadOnlyList<VsixArchiveEntry> _children = Array.Empty<VsixArchiveEntry>();

        internal VsixArchiveEntry(VsixArchive owner, string name, string fullName, bool isDirectory, long length, DateTimeOffset lastWriteTime)
        {
            Owner = owner;
            Name = name;
            FullName = fullName;
            IsDirectory = isDirectory;
            Length = length;
            LastWriteTime = lastWriteTime;
        }

        public VsixArchive Owner { get; internal set; }
        public string Name { get; }
        public string FullName { get; }
        public bool IsDirectory { get; }
        public long Length { get; }
        public DateTimeOffset LastWriteTime { get; }
        public IReadOnlyList<VsixArchiveEntry> Children => _children;

        internal void SetChildren(IReadOnlyList<VsixArchiveEntry> children)
        {
            _children = new ReadOnlyCollection<VsixArchiveEntry>(children.ToList());
        }

        internal void SetOwner(VsixArchive owner)
        {
            Owner = owner;
            foreach (VsixArchiveEntry child in _children)
            {
                child.SetOwner(owner);
            }
        }
    }

    internal static class VsixPathUtilities
    {
        public static string GetPathKey(string value)
        {
            byte[] valueBytes = Encoding.UTF8.GetBytes(value);

            using SHA256 sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(valueBytes);

            var builder = new StringBuilder(16);
            for (int index = 0; index < 8; index++)
            {
                builder.Append(hashBytes[index].ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
