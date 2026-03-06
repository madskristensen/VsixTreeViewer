using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Linq;
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
        private readonly DTE _dte;
        private readonly string _defaultName;
        private EnvDTE.Project _project;

        public VsixRootNode(IVsHierarchyItem hierarchyItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            EnvDTE.Project project = HierarchyUtilities.GetProject(hierarchyItem);
            _defaultName = project.Name + ".vsix";
            _item = new(this, _defaultName, "root");
            _items = new[] { _item };
            _dte = project.DTE;
            _projectPath = project.FullName;
            _project = project;

            Rebuild(false);
            _dte.Events.BuildEvents.OnBuildProjConfigDone += BuildEvents_OnBuildProjConfigDone;
        }

        private void BuildEvents_OnBuildProjConfigDone(string Project, string ProjectConfig, string Platform, string SolutionConfig, bool Success)
        {
            if (Success && IsMatchingProject(Project))
            {
                Debouncer.Debounce(_projectPath, () => Rebuild(true), 500);
            }
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
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var vsixPath = GetVsixPath();

                await TaskScheduler.Default;

                if (!string.IsNullOrEmpty(vsixPath))
                {
                    var unpackedPath = UnpackVsix(vsixPath, force);

                    if (!string.IsNullOrEmpty(unpackedPath))
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        _item.Rebuild(unpackedPath, vsixPath);
                        return;
                    }
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _item.Rebuild(_defaultName, "root");

            }, VsTaskRunContext.UIThreadIdlePriority).FireAndForget();
        }

        private string GetVsixPath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            EnvDTE.Project project = _project;

            if (project == null || !string.Equals(project.FullName, _projectPath, StringComparison.OrdinalIgnoreCase))
            {
                project = FindProjectRecursive(_dte.Solution.Projects);
                _project = project;
            }

            if (project != null)
            {
                var outputPath = project.ConfigurationManager.ActiveConfiguration.Properties.Item("OutputPath").Value.ToString();
                var binDir = Path.Combine(Path.GetDirectoryName(project.FullName), outputPath);

                return Directory.Exists(binDir) ? Directory.GetFiles(binDir, "*.vsix", SearchOption.TopDirectoryOnly).FirstOrDefault() : null;
            }

            return null;
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

            var path = Path.Combine(Path.GetTempPath(), Vsix.Name, Path.GetFileName(vsixPath));

            if (Directory.Exists(path))
            {
                if (!force && Directory.GetLastWriteTime(path) > File.GetLastWriteTime(vsixPath))
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
                return path;
            }
            catch (IOException ex)
            {
                ex.Log();
                return null;
            }
        }

        public void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void Dispose()
        {
            _dte.Events.BuildEvents.OnBuildProjConfigDone -= BuildEvents_OnBuildProjConfigDone;
            _item?.Dispose();
        }
    }
}
