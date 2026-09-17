namespace VsixTreeViewer.Test;

[TestClass]
public sealed class VsixOutputLocatorTests
{
    private string _testDirectory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), nameof(VsixOutputLocatorTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void ResolvesRelativeOutputDirectory()
    {
        string result = VsixOutputLocator.GetOutputDirectory(CreateOptions(outputPath: @"bin\Debug"));

        Assert.AreEqual(Path.Combine(_testDirectory, "bin", "Debug"), result);
    }

    [TestMethod]
    public void PrefersExplicitRelativeTargetContainer()
    {
        string explicitPath = CreateFile(@"artifacts\package.vsix");
        CreateFile(@"bin\Debug\newer.vsix", DateTime.UtcNow.AddMinutes(1));

        VsixOutputLocatorOptions options = CreateOptions(
            outputPath: @"bin\Debug",
            targetVsixContainer: @"artifacts\package.vsix");

        Assert.AreEqual(explicitPath, VsixOutputLocator.FindVsix(options));
    }

    [TestMethod]
    public void PrefersExplicitAbsoluteTargetContainer()
    {
        string explicitPath = CreateFile(@"artifacts\package.vsix");

        VsixOutputLocatorOptions options = CreateOptions(
            outputPath: @"bin\Debug",
            targetVsixContainer: explicitPath);

        Assert.AreEqual(explicitPath, VsixOutputLocator.FindVsix(options));
    }

    [TestMethod]
    public void ResolvesTargetContainerNameInOutputDirectory()
    {
        string expected = CreateFile(@"bin\Release\Custom.vsix");

        VsixOutputLocatorOptions options = CreateOptions(
            outputPath: @"bin\Release",
            targetVsixContainerName: "Custom.vsix");

        Assert.AreEqual(expected, VsixOutputLocator.FindVsix(options));
    }

    [TestMethod]
    public void FindsSdkStyleTargetFrameworkOutput()
    {
        string expected = CreateFile(@"bin\Debug\net48\Extension.vsix");

        VsixOutputLocatorOptions options = CreateOptions(
            outputPath: @"bin\Debug",
            targetFramework: "net48");

        Assert.AreEqual(expected, VsixOutputLocator.FindVsix(options));
    }

    [TestMethod]
    public void FallsBackToNestedOutputDirectory()
    {
        string expected = CreateFile(@"bin\Debug\nested\deeper\Extension.vsix");

        Assert.AreEqual(expected, VsixOutputLocator.FindVsix(CreateOptions(outputPath: @"bin\Debug")));
    }

    [TestMethod]
    public void PreferredNameWinsOverNewerUnrelatedPackage()
    {
        string preferred = CreateFile(@"bin\Debug\Extension.vsix", DateTime.UtcNow);
        CreateFile(@"bin\Debug\Unrelated.vsix", DateTime.UtcNow.AddMinutes(1));

        VsixOutputLocatorOptions options = CreateOptions(
            outputPath: @"bin\Debug",
            preferredFileNames: new[] { "Extension.vsix" });

        Assert.AreEqual(preferred, VsixOutputLocator.FindVsix(options));
    }

    [TestMethod]
    public void NewestPackageWinsWhenNoNameIsPreferred()
    {
        CreateFile(@"bin\Debug\Older.vsix", DateTime.UtcNow);
        string newest = CreateFile(@"bin\Debug\Newest.vsix", DateTime.UtcNow.AddMinutes(1));

        Assert.AreEqual(newest, VsixOutputLocator.FindVsix(CreateOptions(outputPath: @"bin\Debug")));
    }

    [TestMethod]
    public void ReturnsNullWhenOutputDoesNotExist()
    {
        Assert.IsNull(VsixOutputLocator.FindVsix(CreateOptions(outputPath: @"bin\Missing")));
    }

    private VsixOutputLocatorOptions CreateOptions(
        string outputPath,
        string? targetFramework = null,
        string? targetVsixContainer = null,
        string? targetVsixContainerName = null,
        IReadOnlyCollection<string>? preferredFileNames = null)
    {
        return new VsixOutputLocatorOptions
        {
            ProjectDirectory = _testDirectory,
            OutputPath = outputPath,
            TargetFramework = targetFramework,
            TargetVsixContainer = targetVsixContainer,
            TargetVsixContainerName = targetVsixContainerName,
            PreferredFileNames = preferredFileNames ?? Array.Empty<string>()
        };
    }

    private string CreateFile(string relativePath, DateTime? lastWriteTimeUtc = null)
    {
        string path = Path.Combine(_testDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, relativePath);

        if (lastWriteTimeUtc.HasValue)
        {
            File.SetLastWriteTimeUtc(path, lastWriteTimeUtc.Value);
        }

        return path;
    }
}
