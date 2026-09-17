using System.Collections.Concurrent;
using System.IO;
using System.Linq;

namespace VsixTreeViewer
{
    internal static class VsixTemporaryFiles
    {
        private const int SnapshotsToRetainPerProject = 3;
        private static readonly TimeSpan _maximumAge = TimeSpan.FromDays(7);
        private static readonly ConcurrentDictionary<string, byte> _activeSnapshots = new(StringComparer.OrdinalIgnoreCase);

        public static string GetSnapshotDirectory(string sourcePath)
        {
            return Path.Combine(GetRootDirectory(), "Snapshots", VsixPathUtilities.GetPathKey(sourcePath));
        }

        public static string GetMaterializedRoot(string snapshotPath)
        {
            return Path.Combine(GetRootDirectory(), "Files", VsixPathUtilities.GetPathKey(snapshotPath));
        }

        public static void RegisterSnapshot(string snapshotPath)
        {
            if (!string.IsNullOrWhiteSpace(snapshotPath))
            {
                _activeSnapshots[snapshotPath] = 0;
            }
        }

        public static void UnregisterSnapshot(string snapshotPath)
        {
            if (!string.IsNullOrWhiteSpace(snapshotPath))
            {
                _activeSnapshots.TryRemove(snapshotPath, out _);
            }
        }

        public static void TouchMaterializedRoot(string rootPath)
        {
            Directory.SetLastWriteTimeUtc(rootPath, DateTime.UtcNow);
        }

        public static void CleanupStale()
        {
            DateTime cutoff = DateTime.UtcNow - _maximumAge;
            CleanupSnapshots();
            CleanupMaterializedFiles(cutoff);
        }

        private static string GetRootDirectory()
        {
            return Path.Combine(Path.GetTempPath(), Vsix.Name);
        }

        private static void CleanupSnapshots()
        {
            string snapshotsRoot = Path.Combine(GetRootDirectory(), "Snapshots");
            if (!Directory.Exists(snapshotsRoot))
            {
                return;
            }

            foreach (string projectDirectory in Directory.EnumerateDirectories(snapshotsRoot))
            {
                CleanupSnapshotDirectory(projectDirectory);
            }
        }

        internal static void CleanupSnapshotDirectory(string projectDirectory)
        {
            FileInfo[] snapshots;
            try
            {
                snapshots = new DirectoryInfo(projectDirectory)
                    .EnumerateFiles("*.vsix")
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ToArray();
            }
            catch (IOException ex)
            {
                ex.Log();
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                ex.Log();
                return;
            }

            foreach (FileInfo snapshot in snapshots.Skip(SnapshotsToRetainPerProject))
            {
                if (!_activeSnapshots.ContainsKey(snapshot.FullName))
                {
                    TryDeleteFile(snapshot.FullName);
                    TryDeleteDirectory(GetMaterializedRoot(snapshot.FullName));
                }
            }

            TryDeleteEmptyDirectory(projectDirectory);
        }

        private static void CleanupMaterializedFiles(DateTime cutoff)
        {
            string filesRoot = Path.Combine(GetRootDirectory(), "Files");
            if (!Directory.Exists(filesRoot))
            {
                return;
            }

            foreach (DirectoryInfo directory in new DirectoryInfo(filesRoot).EnumerateDirectories())
            {
                if (directory.LastWriteTimeUtc < cutoff)
                {
                    TryDeleteDirectory(directory.FullName);
                }
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException ex)
            {
                ex.Log();
            }
            catch (UnauthorizedAccessException ex)
            {
                ex.Log();
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }

                    Directory.Delete(path, recursive: true);
                }
            }
            catch (IOException ex)
            {
                ex.Log();
            }
            catch (UnauthorizedAccessException ex)
            {
                ex.Log();
            }
        }

        private static void TryDeleteEmptyDirectory(string path)
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(path).Any())
                {
                    Directory.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
