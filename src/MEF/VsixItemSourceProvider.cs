using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Utilities;

namespace VsixTreeViewer.MEF
{
    [Export(typeof(IAttachedCollectionSourceProvider))]
    [Name(nameof(VsixItemSourceProvider))]
    [Order(Before = HierarchyItemsProviderNames.Contains)]
    [AppliesToUIContext(PackageGuids.HasVsixProjectString)]
    internal class VsixItemSourceProvider : IAttachedCollectionSourceProvider, IDisposable
    {
        private readonly Dictionary<string, VsixRootNode> _rootNodes = new();
        private bool _isDisposed;

        public VsixItemSourceProvider()
        {
            VS.Events.SolutionEvents.OnBeforeCloseSolution += OnBeforeCloseSolution;
        }

        private void OnBeforeCloseSolution()
        {
            foreach (var rootNode in _rootNodes.Values)
            {
                rootNode?.Dispose();
            }
            _rootNodes.Clear();
        }

        public IAttachedCollectionSource CreateCollectionSource(object item, string relationshipName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (relationshipName == KnownRelationships.Contains)
            {
                if (item is IVsHierarchyItem hierarchyItem && IsVsixProject(hierarchyItem))
                {
                    string projectPath = GetProjectPath(hierarchyItem);
                    if (!string.IsNullOrEmpty(projectPath))
                    {
                        if (!_rootNodes.TryGetValue(projectPath, out VsixRootNode rootNode))
                        {
                            rootNode = new VsixRootNode(hierarchyItem);
                            _rootNodes[projectPath] = rootNode;
                        }
                        return rootNode;
                    }
                }
                else if (item is VsixItemNode node)
                {
                    return node;
                }
            }

            return null;
        }

        public IEnumerable<IAttachedRelationship> GetRelationships(object item)
        {
            if (item is IVsHierarchyItem hierarchyItem && IsVsixProject(hierarchyItem))
            {
                yield return Relationships.Contains;
            }
        }

        private bool IsVsixProject(IVsHierarchyItem hierarchyItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (!HierarchyUtilities.IsProject(hierarchyItem.HierarchyIdentity) || HierarchyUtilities.IsSolutionFolder(hierarchyItem.HierarchyIdentity))
                {
                    return false;
                }

                IVsHierarchy hierarchy = hierarchyItem.GetHierarchy();

                if (hierarchy.TryGetItemProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_ProjectDir, out string projectDir))
                {
                    var vsixPath = Path.Combine(projectDir, "source.extension.vsixmanifest");
                    return File.Exists(vsixPath);
                }
            }
            catch (Exception ex)
            {
                ex.Log();
            }

            return false;
        }

        private string GetProjectPath(IVsHierarchyItem hierarchyItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                IVsHierarchy hierarchy = hierarchyItem.GetHierarchy();

                if (hierarchy.TryGetItemProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_SaveName, out string projectPath)
                    && !string.IsNullOrWhiteSpace(projectPath))
                {
                    return projectPath;
                }

                EnvDTE.Project project = HierarchyUtilities.GetProject(hierarchyItem);
                return project?.FullName;
            }
            catch (Exception ex)
            {
                ex.Log();
                return null;
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            VS.Events.SolutionEvents.OnBeforeCloseSolution -= OnBeforeCloseSolution;
            OnBeforeCloseSolution();
        }
    }
}
