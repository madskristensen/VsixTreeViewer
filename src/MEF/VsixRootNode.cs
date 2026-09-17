using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Threading;
using VsixTreeViewer.MEF;

namespace VsixTreeViewer
{
    internal class VsixRootNode : IAttachedCollectionSource, INotifyPropertyChanged, IDisposable
    {
        private readonly VsixItemNode _item;
        private readonly IEnumerable _items;
        private readonly string _projectPath;
        private readonly string _projectDirectory;
        private readonly DTE _dte;
        private readonly string _defaultName;
        private readonly object _watcherLock = new();
        private readonly object _snapshotLock = new();
        private EnvDTE.Project _project;
        private FileSystemWatcher _vsixWatcher;
        private string _watchedDirectory;
        private string _snapshotPath;
        private string _vsixPath;
        private volatile bool _isBuilding;
        private volatile bool _isDisposed;

        public VsixRootNode(IVsHierarchyItem hierarchyItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            EnvDTE.Project project = HierarchyUtilities.GetProject(hierarchyItem);
            _defaultName = project.Name + ".vsix";
            _item = new(this, _defaultName, "root");
            _items = new[] { _item };
            _dte = project.DTE;
            _projectPath = project.FullName;
            _projectDirectory = Path.GetDirectoryName(_projectPath);
            _project = project;

            Rebuild(false);
            _dte.Events.BuildEvents.OnBuildProjConfigBegin += BuildEvents_OnBuildProjConfigBegin;
            _dte.Events.BuildEvents.OnBuildProjConfigDone += BuildEvents_OnBuildProjConfigDone;
        }

        private void BuildEvents_OnBuildProjConfigBegin(string Project, string ProjectConfig, string Platform, string SolutionConfig)
        {
            if (IsMatchingProject(Project))
            {
                _isBuilding = true;
                Debouncer.Cancel(_projectPath);
            }
        }

        private void BuildEvents_OnBuildProjConfigDone(string Project, string ProjectConfig, string Platform, string SolutionConfig, bool Success)
        {
            if (!IsMatchingProject(Project))
            {
                return;
            }

            _isBuilding = false;

            if (Success)
            {
                ScheduleRebuild(force: true);
            }
            else
            {
                _item.RebuildError(_defaultName, "The project build failed. Fix the build errors and rebuild to inspect the generated VSIX package.");
            }
        }

        private void ScheduleRebuild(bool force)
        {
            if (_isDisposed || _isBuilding)
            {
                return;
            }

            Debouncer.Debounce(_projectPath, () => Rebuild(force), 500);
        }

        private bool IsMatchingProject(string projectFromEvent)
        {
            string projectUniqueName = null;

            try
            {
                projectUniqueName = _project?.UniqueName;
            }
            catch (COMException)
            {
            }

            return ProjectBuildMatcher.IsMatch(_projectPath, projectUniqueName, projectFromEvent);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return null;
            }
        }

        private void Rebuild(bool force)
        {
            if (_isDisposed)
            {
                return;
            }

            ThreadHelper.JoinableTaskFactory.StartOnIdle(async () =>
            {
                try
                {
                    if (_isDisposed)
                    {
                        return;
                    }

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    string outputDirectory = GetOutputDirectory();
                    string vsixPath = GetVsixPath(outputDirectory);
                    UpdateVsixWatcher(outputDirectory);

                    await TaskScheduler.Default;

                    if (!string.IsNullOrEmpty(vsixPath))
                    {
                        string snapshotPath = CreateVsixSnapshot(vsixPath);
                        VsixArchive archive = !string.IsNullOrWhiteSpace(snapshotPath)
                            ? VsixArchive.Load(snapshotPath, vsixPath)
                            : null;

                        if (archive == null)
                        {
                            await ShowInspectionErrorAsync("The generated VSIX package could not be copied to a stable snapshot.");
                            return;
                        }

                        Comparison = CreateComparison(vsixPath, snapshotPath, archive);
                        string tooltip = BuildTooltip(vsixPath, archive.ManifestContent, Comparison);

                        SetActiveSnapshot(snapshotPath);
                        _vsixPath = vsixPath;
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        _item.Rebuild(archive, vsixPath, tooltip);
                        return;
                    }

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    _vsixPath = null;
                    Comparison = null;
                    _item.Rebuild(_defaultName, "root", BuildMissingVsixTooltip(outputDirectory));
                }
                catch (InvalidDataException ex)
                {
                    await ShowInspectionErrorAsync("The generated VSIX package is corrupt or is not a valid ZIP archive.", ex);
                }
                catch (UnauthorizedAccessException ex)
                {
                    await ShowInspectionErrorAsync("Access to the generated VSIX package or its output directory was denied.", ex);
                }
                catch (IOException ex)
                {
                    await ShowInspectionErrorAsync("The generated VSIX package is currently unavailable. It may still be locked by another process.", ex);
                }
                catch (Exception ex)
                {
                    await ShowInspectionErrorAsync("The generated VSIX package could not be inspected. See the Activity Log for details.", ex);
                }

            }, VsTaskRunContext.UIThreadIdlePriority).FireAndForget();
        }

        private async Task ShowInspectionErrorAsync(string message, Exception exception = null)
        {
            exception?.Log();
            SetActiveSnapshot(snapshotPath: null);
            _vsixPath = null;
            Comparison = null;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (!_isDisposed)
            {
                _item.RebuildError(_defaultName, message);
            }
        }

        private string GetOutputDirectory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string outputPath = GetOutputPathFromProject();
            if (string.IsNullOrWhiteSpace(outputPath) || string.IsNullOrWhiteSpace(_projectDirectory))
            {
                return null;
            }

            return Path.GetFullPath(Path.Combine(_projectDirectory, outputPath));
        }

        private string GetVsixPath(string outputDirectory)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string targetVsixContainer = GetTargetVsixContainerPath();
            if (!string.IsNullOrWhiteSpace(targetVsixContainer) && File.Exists(targetVsixContainer))
            {
                return targetVsixContainer;
            }

            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
            {
                return null;
            }

            var candidateDirectories = new List<string> { outputDirectory };
            string targetFramework = GetEvaluatedProjectPropertyValue("TargetFramework");
            if (!string.IsNullOrWhiteSpace(targetFramework))
            {
                candidateDirectories.Add(Path.Combine(outputDirectory, targetFramework));
            }

            string[] candidates = candidateDirectories
                .Where(Directory.Exists)
                .SelectMany(path => Directory.GetFiles(path, "*.vsix", SearchOption.TopDirectoryOnly))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (candidates.Length == 0)
            {
                candidates = Directory.GetFiles(outputDirectory, "*.vsix", SearchOption.AllDirectories);
            }

            if (candidates.Length == 0)
            {
                return null;
            }

            HashSet<string> preferredNames = GetPreferredVsixFileNames();

            return candidates
                .OrderByDescending(path => preferredNames.Contains(Path.GetFileName(path)))
                .ThenByDescending(path => File.GetLastWriteTimeUtc(path))
                .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private string GetTargetVsixContainerPath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string targetVsixContainer = GetEvaluatedProjectPropertyValue("TargetVsixContainer");
            if (!string.IsNullOrWhiteSpace(targetVsixContainer))
            {
                return Path.IsPathRooted(targetVsixContainer)
                    ? Path.GetFullPath(targetVsixContainer)
                    : Path.GetFullPath(Path.Combine(_projectDirectory, targetVsixContainer));
            }

            string targetVsixContainerName = GetEvaluatedProjectPropertyValue("TargetVsixContainerName");
            string outputPath = GetOutputPathFromProject();
            if (string.IsNullOrWhiteSpace(targetVsixContainerName) || string.IsNullOrWhiteSpace(outputPath))
            {
                return null;
            }

            return Path.GetFullPath(Path.Combine(_projectDirectory, outputPath, targetVsixContainerName));
        }

        private HashSet<string> GetPreferredVsixFileNames()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var preferredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                _defaultName,
                Path.GetFileNameWithoutExtension(_projectPath) + ".vsix"
            };

            EnvDTE.Project project = _project ?? FindProjectRecursive(_dte.Solution.Projects);

            AddVsixFileName(preferredNames, GetProjectPropertyValue(project, "TargetVsixContainerName"));
            AddVsixFileName(preferredNames, GetProjectPropertyValue(project, "TargetName"));
            AddVsixFileName(preferredNames, GetProjectPropertyValue(project, "OutputFileName"));
            AddVsixFileName(preferredNames, GetProjectPropertyValue(project, "AssemblyName"));

            return preferredNames;
        }

        private static void AddVsixFileName(ISet<string> fileNames, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string fileName = value.EndsWith(".vsix", StringComparison.OrdinalIgnoreCase)
                ? value
                : value + ".vsix";

            fileNames.Add(fileName);
        }

        private string GetProjectPropertyValue(EnvDTE.Project project, string propertyName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                return project?.Properties?.Item(propertyName)?.Value?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private string GetEvaluatedProjectPropertyValue(string propertyName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            EnvDTE.Project project = _project ?? FindProjectRecursive(_dte.Solution.Projects);

            try
            {
                string value = project?.ConfigurationManager?.ActiveConfiguration?.Properties?.Item(propertyName)?.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            catch
            {
            }

            return GetProjectPropertyValue(project, propertyName);
        }

        private void UpdateVsixWatcher(string outputDirectory)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string normalizedOutputDirectory = NormalizePath(outputDirectory);
            bool watcherMatches = !string.IsNullOrEmpty(normalizedOutputDirectory)
                && string.Equals(_watchedDirectory, normalizedOutputDirectory, StringComparison.OrdinalIgnoreCase);

            if (watcherMatches)
            {
                return;
            }

            DisposeWatcher();

            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
            {
                return;
            }

            var watcher = new FileSystemWatcher(outputDirectory, "*.vsix")
            {
                // Include subdirectories so the .vsix is detected even when it is only
                // emitted into a target-framework subfolder (e.g. bin\Debug\net48).
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.CreationTime
            };

            watcher.Changed += VsixWatcher_Changed;
            watcher.Created += VsixWatcher_Changed;
            watcher.Deleted += VsixWatcher_Changed;
            watcher.Renamed += VsixWatcher_Renamed;
            watcher.EnableRaisingEvents = true;

            lock (_watcherLock)
            {
                _vsixWatcher = watcher;
                _watchedDirectory = normalizedOutputDirectory;
            }
        }

        private void VsixWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            ScheduleRebuild(force: false);
        }

        private void VsixWatcher_Renamed(object sender, RenamedEventArgs e)
        {
            ScheduleRebuild(force: false);
        }

        private void DisposeWatcher()
        {
            FileSystemWatcher watcherToDispose;

            lock (_watcherLock)
            {
                watcherToDispose = _vsixWatcher;
                _vsixWatcher = null;
                _watchedDirectory = null;
            }

            if (watcherToDispose == null)
            {
                return;
            }

            watcherToDispose.EnableRaisingEvents = false;
            watcherToDispose.Changed -= VsixWatcher_Changed;
            watcherToDispose.Created -= VsixWatcher_Changed;
            watcherToDispose.Deleted -= VsixWatcher_Changed;
            watcherToDispose.Renamed -= VsixWatcher_Renamed;
            watcherToDispose.Dispose();
        }

        private string GetOutputPathFromProject()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                EnvDTE.Project project = _project;

                // Check if we need to re-find the project
                bool needsRefresh = project == null;
                if (!needsRefresh)
                {
                    try
                    {
                        // Access to FullName can throw COMException if the project is stale/unloaded
                        needsRefresh = !string.Equals(project.FullName, _projectPath, StringComparison.OrdinalIgnoreCase);
                    }
                    catch (COMException)
                    {
                        // Cached project reference is stale, need to re-find
                        needsRefresh = true;
                    }
                }

                if (needsRefresh)
                {
                    project = FindProjectRecursive(_dte.Solution.Projects);
                    _project = project;
                }

                string outDir = GetEvaluatedProjectPropertyValue("OutDir");
                return !string.IsNullOrWhiteSpace(outDir)
                    ? outDir
                    : GetEvaluatedProjectPropertyValue("OutputPath");
            }
            catch (Exception ex)
            {
                ex.Log();
                return null;
            }
        }

        /// <summary>
        /// Recursively searches for a project by path, including projects nested in solution folders.
        /// </summary>
        private EnvDTE.Project FindProjectRecursive(Projects projects)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            foreach (EnvDTE.Project project in projects)
            {
                EnvDTE.Project found = FindProjectRecursive(project);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Recursively searches within a project (which may be a solution folder) for the target project.
        /// </summary>
        private EnvDTE.Project FindProjectRecursive(EnvDTE.Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (project == null)
            {
                return null;
            }

            try
            {
                // Check if this is our target project
                // Access to FullName/Kind can throw COMException if the project is stale/unloaded
                if (string.Equals(project.FullName, _projectPath, StringComparison.OrdinalIgnoreCase))
                {
                    return project;
                }

                // If this is a solution folder, search its nested projects
                if (project.Kind == EnvDTE.Constants.vsProjectKindSolutionItems)
                {
                    foreach (ProjectItem item in project.ProjectItems)
                    {
                        EnvDTE.Project subProject = item.SubProject;
                        if (subProject != null)
                        {
                            EnvDTE.Project found = FindProjectRecursive(subProject);
                            if (found != null)
                            {
                                return found;
                            }
                        }
                    }
                }
            }
            catch (COMException)
            {
                // Project is unavailable/stale, skip it
            }

            return null;
        }

        public object SourceItem => this;
        public bool HasItems => _item != null;
        public IEnumerable Items => _items;
        internal string VsixPath => _vsixPath;
        internal VsixArchiveComparison Comparison { get; private set; }

        internal void Refresh()
        {
            ScheduleRebuild(force: true);
        }

        internal void RebuildProject()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            EnvDTE.Project project = _project ?? FindProjectRecursive(_dte.Solution.Projects);
            if (project == null)
            {
                throw new InvalidOperationException("The VSIX project is no longer available.");
            }

            _dte.Solution.SolutionBuild.BuildProject(
                _dte.Solution.SolutionBuild.ActiveConfiguration.Name,
                project.UniqueName,
                WaitForBuildToFinish: false);
        }

        private static string CreateVsixSnapshot(string vsixPath)
        {
            if (string.IsNullOrWhiteSpace(vsixPath) || !File.Exists(vsixPath))
            {
                return null;
            }

            const int maxAttempts = 10;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                string sourceStamp = GetVsixStamp(vsixPath);
                string snapshotDirectory = VsixTemporaryFiles.GetSnapshotDirectory(vsixPath);
                string snapshotPath = Path.Combine(snapshotDirectory, VsixPathUtilities.GetPathKey(sourceStamp) + ".vsix");

                try
                {
                    Directory.CreateDirectory(snapshotDirectory);

                    if (File.Exists(snapshotPath))
                    {
                        return snapshotPath;
                    }

                    string temporaryPath = snapshotPath + "." + Path.GetRandomFileName();
                    try
                    {
                        File.Copy(vsixPath, temporaryPath, overwrite: true);

                        if (!string.Equals(sourceStamp, GetVsixStamp(vsixPath), StringComparison.Ordinal))
                        {
                            File.Delete(temporaryPath);
                            System.Threading.Thread.Sleep(250);
                            continue;
                        }

                        File.Move(temporaryPath, snapshotPath);
                        return snapshotPath;
                    }
                    finally
                    {
                        if (File.Exists(temporaryPath))
                        {
                            File.Delete(temporaryPath);
                        }
                    }
                }
                catch (IOException ex)
                {
                    if (File.Exists(snapshotPath))
                    {
                        return snapshotPath;
                    }

                    if (attempt >= maxAttempts)
                    {
                        throw;
                    }

                    System.Threading.Thread.Sleep(250);
                }
            }

            return null;
        }

        private static string GetVsixStamp(string vsixPath)
        {
            FileInfo fileInfo = new(vsixPath);
            return $"{fileInfo.Length}:{fileInfo.LastWriteTimeUtc.Ticks}";
        }

        private static VsixArchiveComparison CreateComparison(string sourcePath, string currentSnapshot, VsixArchive currentArchive)
        {
            string previousSnapshot = VsixTemporaryFiles.GetPreviousSnapshot(sourcePath, currentSnapshot);
            if (string.IsNullOrWhiteSpace(previousSnapshot))
            {
                return null;
            }

            try
            {
                return VsixArchiveComparison.Create(VsixArchive.Load(previousSnapshot), currentArchive);
            }
            catch (InvalidDataException ex)
            {
                ex.Log();
                return null;
            }
            catch (IOException ex)
            {
                ex.Log();
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                ex.Log();
                return null;
            }
        }

        private void SetActiveSnapshot(string snapshotPath)
        {
            string previousSnapshot;

            lock (_snapshotLock)
            {
                if (string.Equals(_snapshotPath, snapshotPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                previousSnapshot = _snapshotPath;
                _snapshotPath = snapshotPath;
            }

            VsixTemporaryFiles.UnregisterSnapshot(previousSnapshot);
            VsixTemporaryFiles.RegisterSnapshot(snapshotPath);
        }

        private static string BuildMissingVsixTooltip(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                return "Build the project to browse its generated VSIX package.";
            }

            return $"Build the project to browse its generated VSIX package.\r\nExpected output folder: {outputDirectory}";
        }

        private static string BuildTooltip(string vsixPath, string manifestContent, VsixArchiveComparison comparison)
        {
            if (string.IsNullOrWhiteSpace(vsixPath) || !File.Exists(vsixPath))
            {
                return BuildMissingVsixTooltip(outputDirectory: null);
            }

            FileInfo fileInfo = new(vsixPath);
            var tooltip = new StringBuilder();

            AppendTooltipLine(tooltip, "VSIX file", fileInfo.Name);
            AppendTooltipLine(tooltip, "Size", fileInfo.Length.ToString("N0") + " bytes");
            AppendTooltipLine(tooltip, "Last updated", fileInfo.LastWriteTime.ToString());
            AppendTooltipLine(tooltip, "Opened entries", "Read-only temporary copies");

            AddManifestMetadata(tooltip, manifestContent);
            AddComparisonMetadata(tooltip, comparison);

            return tooltip.ToString().TrimEnd();
        }

        private static void AddComparisonMetadata(StringBuilder tooltip, VsixArchiveComparison comparison)
        {
            if (comparison == null)
            {
                return;
            }

            AppendTooltipLine(
                tooltip,
                "Changes",
                $"+{comparison.Added.Count:N0}  -{comparison.Removed.Count:N0}  ~{comparison.Changed.Count:N0}");
        }

        private static void AddManifestMetadata(StringBuilder tooltip, string manifestContent)
        {
            if (string.IsNullOrWhiteSpace(manifestContent))
            {
                return;
            }

            try
            {
                AppendTooltipLine(tooltip, "Display name", GetManifestElementValue(manifestContent, "DisplayName"));
                AppendTooltipLine(tooltip, "ID", GetManifestAttributeValue(manifestContent, "Identity", "Id"));
                AppendTooltipLine(tooltip, "Version", GetManifestAttributeValue(manifestContent, "Identity", "Version"));
                AppendTooltipLine(tooltip, "Publisher", GetManifestAttributeValue(manifestContent, "Identity", "Publisher"));

                List<string> installationTargets = GetInstallationTargets(manifestContent);
                if (installationTargets.Count > 0)
                {
                    AppendTooltipLine(tooltip, "Targets", string.Join(", ", installationTargets));
                }

                int assetCount = CountManifestElements(manifestContent, "Asset");
                if (assetCount > 0)
                {
                    AppendTooltipLine(tooltip, "Assets", assetCount.ToString());
                }
            }
            catch (Exception ex)
            {
                ex.Log();
            }
        }

        private static string GetManifestElementValue(string manifestContent, string elementName)
        {
            Match match = Regex.Match(
                manifestContent,
                $@"<(?:(?:\w+):)?{Regex.Escape(elementName)}\b[^>]*>(?<value>.*?)</(?:(?:\w+):)?{Regex.Escape(elementName)}>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            return match.Success ? CleanManifestValue(match.Groups["value"].Value) : null;
        }

        private static string GetManifestAttributeValue(string manifestContent, string elementName, string attributeName)
        {
            Match match = Regex.Match(
                manifestContent,
                $@"<(?:(?:\w+):)?{Regex.Escape(elementName)}\b[^>]*\b{Regex.Escape(attributeName)}\s*=\s*""(?<value>[^""]*)""[^>]*/?>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            return match.Success ? CleanManifestValue(match.Groups["value"].Value) : null;
        }

        private static List<string> GetInstallationTargets(string manifestContent)
        {
            MatchCollection matches = Regex.Matches(
                manifestContent,
                @"<(?:(?:\w+):)?InstallationTarget\b(?<attributes>[^>]*)>(?<content>.*?)</(?:(?:\w+):)?InstallationTarget>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            var targets = new List<string>();

            foreach (Match match in matches)
            {
                string attributes = match.Groups["attributes"].Value;
                string content = match.Groups["content"].Value;
                string targetId = GetAttributeValue(attributes, "Id");
                string version = GetAttributeValue(attributes, "Version");
                string architecture = GetManifestElementValue(content, "ProductArchitecture");

                string target = string.Join(" ", new[] { targetId, version, architecture }.Where(part => !string.IsNullOrWhiteSpace(part)));
                if (!string.IsNullOrWhiteSpace(target))
                {
                    targets.Add(target);
                }
            }

            return targets;
        }

        private static string GetAttributeValue(string attributes, string attributeName)
        {
            Match match = Regex.Match(
                attributes ?? string.Empty,
                $@"\b{Regex.Escape(attributeName)}\s*=\s*""(?<value>[^""]*)""",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            return match.Success ? CleanManifestValue(match.Groups["value"].Value) : null;
        }

        private static int CountManifestElements(string manifestContent, string elementName)
        {
            return Regex.Matches(
                manifestContent,
                $@"<(?:(?:\w+):)?{Regex.Escape(elementName)}\b",
                RegexOptions.IgnoreCase | RegexOptions.Singleline).Count;
        }

        private static string CleanManifestValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value
                .Replace("\r", string.Empty)
                .Replace("\n", " ")
                .Replace("\t", " ")
                .Trim();
        }

        private static void AppendTooltipLine(StringBuilder tooltip, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            tooltip.Append(label);
            tooltip.Append(": ");
            tooltip.AppendLine(value.Trim());
        }

        public void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            Debouncer.Cancel(_projectPath);
            SetActiveSnapshot(snapshotPath: null);
            _dte.Events.BuildEvents.OnBuildProjConfigBegin -= BuildEvents_OnBuildProjConfigBegin;
            _dte.Events.BuildEvents.OnBuildProjConfigDone -= BuildEvents_OnBuildProjConfigDone;
            DisposeWatcher();
            _item?.Dispose();
        }
    }
}
