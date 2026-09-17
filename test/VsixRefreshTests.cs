using System.IO.Compression;

namespace VsixTreeViewer.Test;

[TestClass]
public sealed class VsixRefreshTests
{
    [TestMethod]
    public void MatchesRelativeBuildEventProjectName()
    {
        Assert.IsTrue(ProjectBuildMatcher.IsMatch(
            @"C:\repos\Extension\src\Extension.csproj",
            @"src\Extension.csproj",
            @"src\Extension.csproj"));
    }

    [TestMethod]
    public void FallsBackToProjectFileNameForBuildEvents()
    {
        Assert.IsTrue(ProjectBuildMatcher.IsMatch(
            @"C:\repos\Extension\src\Extension.csproj",
            projectUniqueName: null,
            @"Extension.csproj"));
    }

    [TestMethod]
    public void SameEntryPathFromNewSnapshotHasNewIdentity()
    {
        string directory = Path.Combine(Path.GetTempPath(), nameof(VsixRefreshTests), Guid.NewGuid().ToString("N"));
        string firstPath = Path.Combine(directory, "first.vsix");
        string secondPath = Path.Combine(directory, "second.vsix");
        Directory.CreateDirectory(directory);

        try
        {
            CreateArchive(firstPath, "first");
            CreateArchive(secondPath, "second");

            VsixArchiveEntry firstEntry = VsixArchive.Load(firstPath).Root.Children.Single();
            VsixArchiveEntry secondEntry = VsixArchive.Load(secondPath).Root.Children.Single();

            Assert.AreEqual(firstEntry.FullName, secondEntry.FullName);
            Assert.AreNotEqual(firstEntry.Identity, secondEntry.Identity);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CreateArchive(string path, string content)
    {
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry("content.txt");
        using StreamWriter writer = new(entry.Open());
        writer.Write(content);
    }
}
