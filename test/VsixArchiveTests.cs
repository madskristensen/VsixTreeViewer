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

    [TestMethod]
    public void SkipsUnsafeEntryPaths()
    {
        string archivePath = CreateArchive(
            ("safe.txt", "safe"),
            ("../outside.txt", "outside"),
            ("folder/./hidden.txt", "hidden"),
            (@"C:\absolute.txt", "absolute"));

        try
        {
            VsixArchive archive = VsixArchive.Load(archivePath);

            Assert.HasCount(1, archive.Root.Children);
            Assert.AreEqual("safe.txt", archive.Root.Children.Single().FullName);
        }
        finally
        {
            DeleteArchiveDirectory(archivePath);
        }
    }

    [TestMethod]
    public void NormalizesBackslashEntryPaths()
    {
        string archivePath = CreateArchive((@"folder\content.txt", "content"));

        try
        {
            VsixArchive archive = VsixArchive.Load(archivePath);
            VsixArchiveEntry file = archive.Root.Children.Single().Children.Single();

            Assert.AreEqual("folder/content.txt", file.FullName);
            Assert.AreEqual("content", File.ReadAllText(archive.Materialize(file)));
        }
        finally
        {
            DeleteArchiveDirectory(archivePath);
        }
    }

    [TestMethod]
    public void SupportsEmptyArchiveWithoutManifest()
    {
        string archivePath = CreateArchive();

        try
        {
            VsixArchive archive = VsixArchive.Load(archivePath);

            Assert.IsEmpty(archive.Root.Children);
            Assert.IsNull(archive.FindManifestEntry());
            Assert.IsNull(archive.ManifestContent);
        }
        finally
        {
            DeleteArchiveDirectory(archivePath);
        }
    }

    [TestMethod]
    public void KeepsFirstDuplicateEntryConsistently()
    {
        string archivePath = CreateArchive(
            ("duplicate.txt", "first"),
            ("duplicate.txt", "second"));

        try
        {
            VsixArchive archive = VsixArchive.Load(archivePath);
            VsixArchiveEntry entry = archive.Root.Children.Single();

            Assert.AreEqual("first", File.ReadAllText(archive.Materialize(entry)));
            Assert.AreEqual("first".Length, entry.Length);
        }
        finally
        {
            DeleteArchiveDirectory(archivePath);
        }
    }

    [TestMethod]
    public void MissingEntryDuringMaterializationThrows()
    {
        string archivePath = CreateArchive(("content.txt", "content"));

        try
        {
            VsixArchive archive = VsixArchive.Load(archivePath);
            VsixArchiveEntry entry = archive.Root.Children.Single();

            using (ZipArchive replacement = ZipFile.Open(archivePath, ZipArchiveMode.Update))
            {
                replacement.GetEntry("content.txt")!.Delete();
            }

            Assert.ThrowsExactly<InvalidDataException>(() => archive.Materialize(entry));
        }
        finally
        {
            DeleteArchiveDirectory(archivePath);
        }
    }

    [TestMethod]
    public void ExtractionOverwritesFilesAndLeavesThemWritable()
    {
        string archivePath = CreateArchive(("folder/content.txt", "new"));
        string extractionPath = Path.Combine(Path.GetDirectoryName(archivePath)!, "extracted");
        string extractedFile = Path.Combine(extractionPath, "folder", "content.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(extractedFile)!);
        File.WriteAllText(extractedFile, "old");
        File.SetAttributes(extractedFile, FileAttributes.ReadOnly);

        try
        {
            VsixArchive.Load(archivePath).ExtractToDirectory(extractionPath);

            Assert.AreEqual("new", File.ReadAllText(extractedFile));
            Assert.IsFalse(new FileInfo(extractedFile).IsReadOnly);
        }
        finally
        {
            File.SetAttributes(extractedFile, FileAttributes.Normal);
            DeleteArchiveDirectory(archivePath);
        }
    }

    [TestMethod]
    public void SupportsUnicodeFileNames()
    {
        string archivePath = CreateArchive(("folder/日本語.txt", "content"));

        try
        {
            VsixArchiveEntry entry = VsixArchive.Load(archivePath).Root.Children.Single().Children.Single();
            Assert.AreEqual("日本語.txt", entry.Name);
        }
        finally
        {
            DeleteArchiveDirectory(archivePath);
        }
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);
        using StreamWriter writer = new(entry.Open());
        writer.Write(content);
    }

    private static string CreateArchive(params (string Path, string Content)[] entries)
    {
        string directory = Path.Combine(Path.GetTempPath(), nameof(VsixArchiveTests), Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(directory, "sample.vsix");
        Directory.CreateDirectory(directory);

        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            foreach ((string path, string content) in entries)
            {
                WriteEntry(archive, path, content);
            }
        }

        return archivePath;
    }

    private static void DeleteArchiveDirectory(string archivePath)
    {
        DeleteDirectory(VsixTemporaryFiles.GetMaterializedRoot(archivePath));
        string directory = Path.GetDirectoryName(archivePath)!;
        DeleteDirectory(directory);
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(directory, recursive: true);
        }
    }
}
