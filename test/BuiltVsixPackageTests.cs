using System.IO.Compression;

namespace VsixTreeViewer.Test;

[TestClass]
public sealed class BuiltVsixPackageTests
{
    [TestMethod]
    public void BuiltPackageContainsRequiredExtensionAssets()
    {
        string packagePath = Path.Combine(AppContext.BaseDirectory, "VsixTreeViewer.vsix");
        Assert.IsTrue(File.Exists(packagePath), $"Built VSIX was not copied to the test output: {packagePath}");

        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        string[] entries = archive.Entries.Select(entry => entry.FullName).ToArray();

        CollectionAssert.IsSubsetOf(
            new[]
            {
                "extension.vsixmanifest",
                "VsixTreeViewer.dll",
                "VsixTreeViewer.pkgdef",
                "Resources/LICENSE.txt",
                "Resources/Icon.png",
                "[Content_Types].xml"
            },
            entries);
    }

    [TestMethod]
    public void BuiltPackageManifestDescribesThisExtension()
    {
        string packagePath = Path.Combine(AppContext.BaseDirectory, "VsixTreeViewer.vsix");
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry manifest = archive.GetEntry("extension.vsixmanifest")!;
        using var reader = new StreamReader(manifest.Open());
        string content = reader.ReadToEnd();

        StringAssert.Contains(content, "VsixTreeViewer.8bc7b2af-9ddc-4b5d-9983-6a980b3d0243");
        StringAssert.Contains(content, "Microsoft.VisualStudio.MefComponent");
        StringAssert.Contains(content, "Microsoft.VisualStudio.VsPackage");
    }
}
