using System.IO.Compression;

namespace VsixTreeViewer.Test;

[TestClass]
public sealed class VsixArchiveComparisonTests
{
    [TestMethod]
    public void ReportsAddedRemovedAndChangedEntries()
    {
        string directory = Path.Combine(Path.GetTempPath(), nameof(VsixArchiveComparisonTests), Guid.NewGuid().ToString("N"));
        string previousPath = Path.Combine(directory, "previous.vsix");
        string currentPath = Path.Combine(directory, "current.vsix");
        var timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Directory.CreateDirectory(directory);

        try
        {
            CreateArchive(previousPath, timestamp, ("same.txt", "same"), ("removed.txt", "removed"), ("changed.txt", "old"));
            CreateArchive(currentPath, timestamp, ("same.txt", "same"), ("added.txt", "added"), ("changed.txt", "new content"));

            VsixArchiveComparison comparison = VsixArchiveComparison.Create(
                VsixArchive.Load(previousPath),
                VsixArchive.Load(currentPath));

            CollectionAssert.AreEqual(new[] { "added.txt" }, comparison.Added.ToArray());
            CollectionAssert.AreEqual(new[] { "removed.txt" }, comparison.Removed.ToArray());
            CollectionAssert.AreEqual(new[] { "changed.txt" }, comparison.Changed.ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CreateArchive(string path, DateTimeOffset timestamp, params (string Path, string Content)[] entries)
    {
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);

        foreach ((string entryPath, string content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryPath);
            entry.LastWriteTime = timestamp;
            using StreamWriter writer = new(entry.Open());
            writer.Write(content);
        }
    }
}
