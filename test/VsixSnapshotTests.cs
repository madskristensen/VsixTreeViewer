using System.Collections.Concurrent;

namespace VsixTreeViewer.Test;

[TestClass]
public sealed class VsixSnapshotTests
{
    private string _testDirectory = null!;
    private string _sourcePath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), nameof(VsixSnapshotTests), Guid.NewGuid().ToString("N"));
        _sourcePath = Path.Combine(_testDirectory, "source.vsix");
        Directory.CreateDirectory(_testDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        string snapshotDirectory = VsixTemporaryFiles.GetSnapshotDirectory(_sourcePath);
        if (Directory.Exists(snapshotDirectory))
        {
            Directory.Delete(snapshotDirectory, recursive: true);
        }

        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void MissingSourceReturnsNull()
    {
        Assert.IsNull(VsixSnapshot.Create(_sourcePath));
    }

    [TestMethod]
    public void CreatesStableCopy()
    {
        File.WriteAllText(_sourcePath, "first");

        string snapshotPath = VsixSnapshot.Create(_sourcePath);

        Assert.IsTrue(File.Exists(snapshotPath));
        Assert.AreEqual("first", File.ReadAllText(snapshotPath));
        Assert.AreNotEqual(_sourcePath, snapshotPath);
    }

    [TestMethod]
    public void ReusesSnapshotWhenSourceIsUnchanged()
    {
        File.WriteAllText(_sourcePath, "same");

        string first = VsixSnapshot.Create(_sourcePath);
        string second = VsixSnapshot.Create(_sourcePath);

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void CreatesNewSnapshotWhenSourceChanges()
    {
        File.WriteAllText(_sourcePath, "first");
        string first = VsixSnapshot.Create(_sourcePath);

        File.WriteAllText(_sourcePath, "second version");
        File.SetLastWriteTimeUtc(_sourcePath, DateTime.UtcNow.AddSeconds(1));
        string second = VsixSnapshot.Create(_sourcePath);

        Assert.AreNotEqual(first, second);
        Assert.AreEqual("first", File.ReadAllText(first));
        Assert.AreEqual("second version", File.ReadAllText(second));
    }

    [TestMethod]
    public void DoesNotReuseSnapshotWhenContentChangesButMetadataMatches()
    {
        DateTime timestamp = DateTime.UtcNow.AddMinutes(-1);
        File.WriteAllText(_sourcePath, "first");
        File.SetLastWriteTimeUtc(_sourcePath, timestamp);
        string first = VsixSnapshot.Create(_sourcePath);

        File.WriteAllText(_sourcePath, "other");
        File.SetLastWriteTimeUtc(_sourcePath, timestamp);
        string second = VsixSnapshot.Create(_sourcePath);

        Assert.AreNotEqual(first, second);
        Assert.AreEqual("first", File.ReadAllText(first));
        Assert.AreEqual("other", File.ReadAllText(second));
    }

    [TestMethod]
    public void ConcurrentCreationReturnsOneStableSnapshot()
    {
        File.WriteAllText(_sourcePath, new string('x', 32 * 1024));
        var paths = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, 20, _ =>
        {
            try
            {
                paths.Add(VsixSnapshot.Create(_sourcePath));
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.IsEmpty(exceptions);
        Assert.HasCount(1, paths.Distinct().ToArray());
        Assert.AreEqual(File.ReadAllText(_sourcePath), File.ReadAllText(paths.First()));
        Assert.HasCount(1, Directory.GetFiles(VsixTemporaryFiles.GetSnapshotDirectory(_sourcePath), "*.vsix"));
    }
}
