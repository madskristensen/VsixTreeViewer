using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
        private EnvDTE.Project _project;
        private FileSystemWatcher _vsixWatcher;
        private string _watchedDirectory;

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
            _dte.Events.BuildEvents.OnBuildProjConfigDone += BuildEvents_OnBuildProjConfigDone;
        }

        private void BuildEvents_OnBuildProjConfigDone(string Project, string ProjectConfig, string Platform, string SolutionConfig, bool Success)
        {
            if (Success && IsMatchingProject(Project))
            {
                ScheduleRebuild(force: true);
            }
        }

        private void ScheduleRebuild(bool force)
        {
            Debouncer.Debounce(_projectPath, () => Rebuild(force), 500);
        }

        private bool IsMatchingProject(string projectFromEvent)
        {
            if (string.IsNullOrWhiteSpace(projectFromEvent))
            {
                return false;
            }

            string trackedProject = NormalizePath(_projectPath);
            string eventProject = NormalizePath(projectFromEvent);

            if (!string.IsNullOrEmpty(trackedProject) && !string.IsNullOrEmpty(eventProject))
            {
                return string.Equals(trackedProject, eventProject, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(Path.GetFileName(_projectPath), Path.GetFileName(projectFromEvent), StringComparison.OrdinalIgnoreCase);
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
            ThreadHelper.JoinableTaskFactory.StartOnIdle(async () =>
            {
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    string outputDirectory = GetOutputDirectory();
                    string vsixPath = GetVsixPath(outputDirectory);
                    UpdateVsixWatcher(outputDirectory);

                    await TaskScheduler.Default;

                    if (!string.IsNullOrEmpty(vsixPath))
                    {
                        string unpackedPath = UnpackVsix(vsixPath, force);
                        string tooltip = BuildTooltip(vsixPath, unpackedPath);

                        if (!string.IsNullOrEmpty(unpackedPath))
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            _item.Rebuild(unpackedPath, vsixPath, tooltip);
                            return;
                        }

                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        _item.Rebuild(_defaultName, "root", tooltip);
                        return;
                    }

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    _item.Rebuild(_defaultName, "root", BuildMissingVsixTooltip(outputDirectory));
                }
                catch (Exception ex)
                {
                    ex.Log();
                }

            }, VsTaskRunContext.UIThreadIdlePriority).FireAndForget();
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

            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
            {
                return null;
            }

            string[] candidates = Directory.GetFiles(outputDirectory, "*.vsix", SearchOption.TopDirectoryOnly);
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

        private HashSet<string> GetPreferredVsixFileNames()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var preferredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                _defaultName,
                Path.GetFileNameWithoutExtension(_projectPath) + ".vsix"
            };

            EnvDTE.Project project = _project ?? FindProjectRecursive(_dte.Solution.Projects);

            AddVsixFileName(preferredNames, GetProjectPropertyValue(project, "TargetFileName"));
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
                IncludeSubdirectories = false,
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

                if (project == null || !string.Equals(project.FullName, _projectPath, StringComparison.OrdinalIgnoreCase))
                {
                    project = FindProjectRecursive(_dte.Solution.Projects);
                    _project = project;
                }

                return project?.ConfigurationManager?.ActiveConfiguration?.Properties?.Item("OutputPath")?.Value?.ToString();
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

            // Check if this is our target project
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

            return null;
        }

        public object SourceItem => this;
        public bool HasItems => _item != null;
        public IEnumerable Items => _items;

        private string UnpackVsix(string vsixPath, bool force)
        {
            if (!File.Exists(vsixPath))
            {
                return null;
            }

            string path = GetExtractionPath(vsixPath);
            string currentStamp = GetVsixStamp(vsixPath);

            if (Directory.Exists(path))
            {
                if (!force && IsExtractionCurrent(path, currentStamp))
                {
                    return path;
                }

                try
                {
                    Directory.Delete(path, true);
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

            try
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(vsixPath, path);
                WriteExtractionStamp(path, currentStamp);
                return path;
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

        private static string GetExtractionPath(string vsixPath)
        {
            return Path.Combine(Path.GetTempPath(), Vsix.Name, GetPathKey(vsixPath));
        }

        private static string GetPathKey(string path)
        {
            byte[] pathBytes = Encoding.UTF8.GetBytes(path);
            byte[] hashBytes;

            using (SHA256 sha256 = SHA256.Create())
            {
                hashBytes = sha256.ComputeHash(pathBytes);
            }

            var builder = new StringBuilder(16);
            for (int i = 0; i < 8; i++)
            {
                builder.Append(hashBytes[i].ToString("x2"));
            }

            return builder.ToString();
        }

        private static string GetStampPath(string extractionPath)
        {
            return Path.Combine(extractionPath, ".vsixstamp");
        }

        private static string GetVsixStamp(string vsixPath)
        {
            FileInfo fileInfo = new(vsixPath);
            return $"{fileInfo.Length}:{fileInfo.LastWriteTimeUtc.Ticks}";
        }

        private static bool IsExtractionCurrent(string extractionPath, string currentStamp)
        {
            string stampPath = GetStampPath(extractionPath);
            return File.Exists(stampPath) && string.Equals(File.ReadAllText(stampPath), currentStamp, StringComparison.Ordinal);
        }

        private static void WriteExtractionStamp(string extractionPath, string currentStamp)
        {
            File.WriteAllText(GetStampPath(extractionPath), currentStamp);
        }

        private static string BuildMissingVsixTooltip(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                return "Build the project to browse its generated VSIX package.";
            }

            return $"Build the project to browse its generated VSIX package.\r\nExpected output folder: {outputDirectory}";
        }

        private static string BuildTooltip(string vsixPath, string extractedPath)
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

            AddManifestMetadata(tooltip, extractedPath);

            return tooltip.ToString().TrimEnd();
        }

        private static void AddManifestMetadata(StringBuilder tooltip, string extractedPath)
        {
            string manifestPath = GetManifestPath(extractedPath);
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                return;
            }

            try
            {
                string manifestContent = File.ReadAllText(manifestPath);

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

        private static string GetManifestPath(string extractedPath)
        {
            if (string.IsNullOrWhiteSpace(extractedPath) || !Directory.Exists(extractedPath))
            {
                return null;
            }

            return Directory.GetFiles(extractedPath, "*.vsixmanifest", SearchOption.TopDirectoryOnly).FirstOrDefault();
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
            _dte.Events.BuildEvents.OnBuildProjConfigDone -= BuildEvents_OnBuildProjConfigDone;
            DisposeWatcher();
            _item?.Dispose();
        }
    }
}
