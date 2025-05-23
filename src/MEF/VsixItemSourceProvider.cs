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
    internal class VsixItemSourceProvider : IAttachedCollectionSourceProvider
    {
        private VsixRootNode _rootNode;

        public VsixItemSourceProvider()
        {
            VS.Events.SolutionEvents.OnBeforeCloseSolution += OnBeforeCloseSolution;
        }

        private void OnBeforeCloseSolution()
        {
            _rootNode?.Dispose();
            _rootNode = null;
        }

        public IAttachedCollectionSource CreateCollectionSource(object item, string relationshipName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (relationshipName == KnownRelationships.Contains)
            {
                if (item is IVsHierarchyItem hierarchyItem && IsVsixProject(hierarchyItem))
                {
                    return _rootNode ??= new VsixRootNode(hierarchyItem);
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
    }
}
