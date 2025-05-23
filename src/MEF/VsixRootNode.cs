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
        private readonly string _projectPath;
        private readonly DTE _dte;
        private readonly string _defaultName;

        public VsixRootNode(IVsHierarchyItem hierarchyItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            EnvDTE.Project project = HierarchyUtilities.GetProject(hierarchyItem);
            _defaultName = project.Name + ".vsix";
            _item = new(this, _defaultName, "root");
            _dte = project.DTE;
            _projectPath = project.FullName;

            Debouncer.Debounce(_projectPath, Rebuild, 500);
            _dte.Events.BuildEvents.OnBuildProjConfigDone += BuildEvents_OnBuildProjConfigDone;
        }

        private void BuildEvents_OnBuildProjConfigDone(string Project, string ProjectConfig, string Platform, string SolutionConfig, bool Success)
        {
            if (Success && _projectPath.EndsWith(Project))
            {
                Debouncer.Debounce(_projectPath, Rebuild, 500);
            }
        }

        private void Rebuild()
        {
            ThreadHelper.JoinableTaskFactory.StartOnIdle(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var vsixPath = GetVsixPath();

                await TaskScheduler.Default;

                if (!string.IsNullOrEmpty(vsixPath))
                {
                    var unpackedPath = UnpackVsix(vsixPath);

                    if (!string.IsNullOrEmpty(unpackedPath))
                    {
                        _item.Rebuild(unpackedPath, vsixPath);
                    }
                }
                else
                {
                    //_item.Dispose();
                    _item.Rebuild(_defaultName, "root");
                }

            }, VsTaskRunContext.UIThreadIdlePriority).FireAndForget();
        }

        private string GetVsixPath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            EnvDTE.Project project = _dte.Solution.Projects.OfType<EnvDTE.Project>().FirstOrDefault(p =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return p.FullName == _projectPath;
            });

            if (project != null)
            {
                var outputPath = project.ConfigurationManager.ActiveConfiguration.Properties.Item("OutputPath").Value.ToString();
                var binDir = Path.Combine(Path.GetDirectoryName(project.FullName), outputPath);

                return Directory.Exists(binDir) ? Directory.GetFiles(binDir, "*.vsix", SearchOption.TopDirectoryOnly).FirstOrDefault() : null;
            }

            return null;
        }

        public object SourceItem => this;
        public bool HasItems => _item != null;
        public IEnumerable Items => new[] { _item };

        private string UnpackVsix(string vsixPath)
        {
            if (!File.Exists(vsixPath))
            {
                return null;
            }

            var path = Path.Combine(Path.GetTempPath(), Vsix.Name, Path.GetFileName(vsixPath));

            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
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

        }
    }
}
