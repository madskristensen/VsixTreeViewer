using System.IO.Compression;

namespace VsixTreeViewer.Test;

[TestClass]
public sealed class VsixBuildRefreshIntegrationTests
{
    [TestMethod]
    public void ConsecutiveBuildRefreshLoadsLatestPackageContents()
    {
        string projectDirectory = Path.Combine(Path.GetTempPath(), nameof(VsixBuildRefreshIntegrationTests), Guid.NewGuid().ToString("N"));
        string packagePath = Path.Combine(projectDirectory, "bin", "Debug", "net48", "Extension.vsix");
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        var dispatcher = new TestDebounceDispatcher();
        var loadedContents = new List<string>();
        var loadedSnapshots = new List<string>();
        var options = new VsixOutputLocatorOptions
        {
            ProjectDirectory = projectDirectory,
            OutputPath = @"bin\Debug",
            TargetFramework = "net48",
            PreferredFileNames = new[] { "Extension.vsix" }
        };

        try
        {
            using var coordinator = new VsixRefreshCoordinator("project", _ =>
            {
                string discoveredPackage = VsixOutputLocator.FindVsix(options);
                string snapshot = VsixSnapshot.Create(discoveredPackage);
                VsixArchive archive = VsixArchive.Load(snapshot, discoveredPackage);
                VsixArchiveEntry entry = archive.Root.Children.Single();
                loadedSnapshots.Add(snapshot);
                loadedContents.Add(File.ReadAllText(archive.Materialize(entry)));
            }, dispatcher);

            CreatePackage(packagePath, "first");
            coordinator.Schedule(force: false);
            dispatcher.RunPending();

            CreatePackage(packagePath, "second version");
            File.SetLastWriteTimeUtc(packagePath, DateTime.UtcNow.AddSeconds(1));
            coordinator.BuildStarted();
            coordinator.BuildCompleted(success: true);
            dispatcher.RunPending();

            CollectionAssert.AreEqual(new[] { "first", "second version" }, loadedContents);
            Assert.AreNotEqual(loadedSnapshots[0], loadedSnapshots[1]);
        }
        finally
        {
            foreach (string snapshot in loadedSnapshots)
            {
                DeleteDirectory(VsixTemporaryFiles.GetMaterializedRoot(snapshot));
            }

            string snapshotDirectory = VsixTemporaryFiles.GetSnapshotDirectory(packagePath);
            DeleteDirectory(snapshotDirectory);
            DeleteDirectory(projectDirectory);
        }
    }

    private static void CreatePackage(string path, string content)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry("content.txt");
        using StreamWriter writer = new(entry.Open());
        writer.Write(content);
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }

    private sealed class TestDebounceDispatcher : IDebounceDispatcher
    {
        private Action? _action;

        public void Debounce(string key, Action action, int milliseconds)
        {
            _action = action;
        }

        public void Cancel(string key)
        {
            _action = null;
        }

        public void RunPending()
        {
            Action? action = _action;
            _action = null;
            action?.Invoke();
        }
    }
}
