namespace VsixTreeViewer.Test;

[TestClass]
public sealed class VsixTemporaryFilesTests
{
    [TestMethod]
    public void RetainsThreeNewestSnapshots()
    {
        string directory = Path.Combine(Path.GetTempPath(), nameof(VsixTemporaryFilesTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            for (int index = 0; index < 5; index++)
            {
                string path = Path.Combine(directory, $"{index}.vsix");
                File.WriteAllText(path, index.ToString());
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(index));
            }

            VsixTemporaryFiles.CleanupSnapshotDirectory(directory);

            CollectionAssert.AreEquivalent(
                new[] { "2.vsix", "3.vsix", "4.vsix" },
                Directory.GetFiles(directory).Select(Path.GetFileName).ToArray());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void RetainsActiveSnapshotBeyondNewestThree()
    {
        string directory = Path.Combine(Path.GetTempPath(), nameof(VsixTemporaryFilesTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string activePath = Path.Combine(directory, "0.vsix");

        try
        {
            for (int index = 0; index < 5; index++)
            {
                string path = Path.Combine(directory, $"{index}.vsix");
                File.WriteAllText(path, index.ToString());
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(index));
            }

            VsixTemporaryFiles.RegisterSnapshot(activePath);
            VsixTemporaryFiles.CleanupSnapshotDirectory(directory);

            CollectionAssert.AreEquivalent(
                new[] { "0.vsix", "2.vsix", "3.vsix", "4.vsix" },
                Directory.GetFiles(directory).Select(Path.GetFileName).ToArray());

            VsixTemporaryFiles.UnregisterSnapshot(activePath);
            VsixTemporaryFiles.CleanupSnapshotDirectory(directory);
            Assert.IsFalse(File.Exists(activePath));
        }
        finally
        {
            VsixTemporaryFiles.UnregisterSnapshot(activePath);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
