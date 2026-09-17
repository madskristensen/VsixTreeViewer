using System.IO.Compression;

namespace VsixTreeViewer.Test;

[TestClass]
public sealed class VsixArchiveTests
{
    [TestMethod]
    public void RejectsInvalidArchive()
    {
        string archivePath = Path.GetTempFileName();

        try
        {
            File.WriteAllText(archivePath, "not a zip archive");
            Assert.ThrowsExactly<InvalidDataException>(() => VsixArchive.Load(archivePath));
        }
        finally
        {
            File.Delete(archivePath);
        }
    }

    [TestMethod]
    public void MaterializesOnlyRequestedEntry()
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), nameof(VsixArchiveTests), Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(testDirectory, "sample.vsix");
        Directory.CreateDirectory(testDirectory);

        try
        {
            using (ZipArchive zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "folder/a.txt", "alpha");
                WriteEntry(zip, "b.txt", "bravo");
            }

            VsixArchive archive = VsixArchive.Load(archivePath);
            VsixArchiveEntry folder = archive.Root.Children.Single(entry => entry.Name == "folder");
            VsixArchiveEntry entry = folder.Children.Single();

            string materializedPath = archive.Materialize(entry);
            string materializedRoot = Directory.GetParent(Path.GetDirectoryName(materializedPath))!.FullName;

            Assert.AreEqual("alpha", File.ReadAllText(materializedPath));
            Assert.IsTrue(new FileInfo(materializedPath).IsReadOnly);
            Assert.IsFalse(File.Exists(Path.Combine(materializedRoot, "b.txt")));

            File.SetAttributes(materializedPath, FileAttributes.Normal);
            Directory.Delete(materializedRoot, recursive: true);
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void FindsManifestAndExtractsPackage()
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), nameof(VsixArchiveTests), Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(testDirectory, "sample.vsix");
        string extractionPath = Path.Combine(testDirectory, "extracted");
        Directory.CreateDirectory(testDirectory);

        try
        {
            using (ZipArchive zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "extension.vsixmanifest", "<PackageManifest />");
                WriteEntry(zip, "folder/content.txt", "content");
            }

            VsixArchive archive = VsixArchive.Load(archivePath);
            VsixArchiveEntry manifest = archive.FindManifestEntry();
            archive.ExtractToDirectory(extractionPath);

            Assert.IsNotNull(manifest);
            Assert.AreEqual("extension.vsixmanifest", manifest.FullName);
            Assert.AreEqual("content", File.ReadAllText(Path.Combine(extractionPath, "folder", "content.txt")));
            Assert.IsFalse(new FileInfo(Path.Combine(extractionPath, "folder", "content.txt")).IsReadOnly);
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);
        using StreamWriter writer = new(entry.Open());
        writer.Write(content);
    }
}
