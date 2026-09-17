using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;

namespace VsixTreeViewer.MEF
{
    [DebuggerDisplay("{Text}")]
    internal class VsixItemNode :
        IAttachedCollectionSource,
        ITreeDisplayItem,
        ITreeDisplayItemWithImages,
        IPrioritizedComparable,
        IBrowsablePattern,
        IInteractionPatternProvider,
        IContextMenuPattern,
        IInvocationPattern,
        ISupportDisposalNotification,
        IDisposable,
        IRefreshPattern
    {
        private static readonly StringComparer _stringComparer = StringComparer.OrdinalIgnoreCase;

        private BulkObservableCollection<VsixItemNode> _children;
        private bool _isLoaded;
        private bool _isLoading;
        private bool _reloadRequested;
        private int _loadGeneration;
        private bool _showVsixIcon;
        private bool _showErrorIcon;
        private readonly object _loadLock = new();
        private CancellationTokenSource _loadCancellationTokenSource;
        private VsixArchiveEntry _archiveEntry;
        private IReadOnlyList<VsixItemNode> _virtualChildren;
        private string _virtualPath;
        private bool _isVirtualDirectory;

        public VsixItemNode(IAttachedCollectionSource source, string outputPath, string vsixPath, string tooltipContent = null)
        {
            SourceItem = source;
            Rebuild(outputPath, vsixPath, tooltipContent);
        }

        private VsixItemNode(IAttachedCollectionSource source, VsixArchiveEntry archiveEntry)
        {
            SourceItem = source;
            _archiveEntry = archiveEntry;
            Text = archiveEntry.Name;
            ToolTipContent = SetArchiveTooltip(archiveEntry);
            HasItems = archiveEntry.IsDirectory && archiveEntry.Children.Count > 0;
        }

        private VsixItemNode(IAttachedCollectionSource source, string text, string tooltip, string virtualPath, bool isDirectory)
        {
            SourceItem = source;
            Text = text;
            ToolTipContent = tooltip;
            _virtualPath = virtualPath;
            _isVirtualDirectory = isDirectory;
        }

        public void Rebuild(VsixArchive archive, string vsixPath, string tooltipContent)
        {
            ResetLoadingState();

            string oldText = Text;
            bool oldIsCut = IsCut;
            bool oldHasItems = HasItems;

            _archiveEntry = archive?.Root;
            Info = null;
            Text = archive != null ? Path.GetFileName(vsixPath) : Path.GetFileName(vsixPath) ?? ".vsix content";
            IsCut = archive == null;
            HasItems = archive?.Root.Children.Count > 0;
            _showVsixIcon = archive != null;
            _showErrorIcon = false;
            ToolTipContent = tooltipContent;

            RaiseChangedProperties(oldText, oldIsCut, oldHasItems);
            RaisePropertyChanged(nameof(ToolTipContent));
            _ = LoadChildrenAsync();
        }

        public void RebuildError(string displayName, string message)
        {
            ResetLoadingState();

            string oldText = Text;
            bool oldIsCut = IsCut;
            bool oldHasItems = HasItems;

            _archiveEntry = null;
            Info = null;
            Text = displayName;
            IsCut = false;
            HasItems = false;
            _showVsixIcon = false;
            _showErrorIcon = true;
            ToolTipContent = message;

            RaiseChangedProperties(oldText, oldIsCut, oldHasItems);
            RaisePropertyChanged(nameof(IconMoniker));
            RaisePropertyChanged(nameof(ExpandedIconMoniker));
            RaisePropertyChanged(nameof(ToolTipContent));
        }

        public void Rebuild(string outputPath, string vsixPath, string tooltipContent = null)
        {
            string newText = !string.IsNullOrEmpty(vsixPath) && File.Exists(vsixPath)
                ? Path.GetFileName(vsixPath)
                : Path.GetFileName(outputPath) ?? ".vsix content";
            bool newIsCut = false;
            FileSystemInfo newInfo;
            bool newHasItems;

            ResetLoadingState();
            _archiveEntry = null;

            if (Directory.Exists(outputPath))
            {
                newInfo = new DirectoryInfo(outputPath);
                newHasItems = CheckHasItemsQuick(newInfo as DirectoryInfo);
            }
            else if (File.Exists(outputPath))
            {
                newInfo = new FileInfo(outputPath);
                newHasItems = false;
            }
            else
            {
                newInfo = null;
                newIsCut = true;
                newHasItems = false;
            }

            string oldText = Text;
            bool oldIsCut = IsCut;
            bool oldHasItems = HasItems;

            Info = newInfo;
            Text = newText;
            IsCut = newIsCut;
            HasItems = newHasItems;
            _showVsixIcon = !string.IsNullOrEmpty(vsixPath) &&
                !string.Equals(vsixPath, "root", StringComparison.OrdinalIgnoreCase) &&
                vsixPath.EndsWith(".vsix", StringComparison.OrdinalIgnoreCase);
            _showErrorIcon = false;

            RaiseChangedProperties(oldText, oldIsCut, oldHasItems);

            if (!string.IsNullOrEmpty(vsixPath) || tooltipContent != null)
            {
                object oldTooltip = ToolTipContent;
                ToolTipContent = tooltipContent ?? SetTooltip(vsixPath);

                if (!Equals(oldTooltip, ToolTipContent))
                {
                    RaisePropertyChanged(nameof(ToolTipContent));
                }
            }
        }

        private void ResetLoadingState()
        {
            lock (_loadLock)
            {
                _isLoaded = false;
                _loadGeneration++;

                if (_isLoading)
                {
                    _reloadRequested = true;
                    _loadCancellationTokenSource?.Cancel();
                }
            }
        }

        private void RaiseChangedProperties(string oldText, bool oldIsCut, bool oldHasItems)
        {
            if (!string.Equals(oldText, Text, StringComparison.Ordinal))
            {
                RaisePropertyChanged(nameof(Text));
            }

            if (oldIsCut != IsCut)
            {
                RaisePropertyChanged(nameof(IsCut));
            }

            if (oldHasItems != HasItems)
            {
                RaisePropertyChanged(nameof(HasItems));
            }
        }

        private bool CheckHasItemsQuick(DirectoryInfo directory)
        {
            if (directory != null)
            {
                try
                {
                    // Quick check - just see if there's at least one item without enumerating all
                    return directory.EnumerateFileSystemInfos().Take(1).Any();
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        public FileSystemInfo Info { get; set; }
        public object SourceItem { get; init; }
        public bool HasItems { get; set; }

        public IEnumerable Items
        {
            get
            {
                if (_virtualChildren != null)
                {
                    return _virtualChildren;
                }

                _children ??= [];

                if (!_isLoaded && !_isLoading && (Info is DirectoryInfo || _archiveEntry?.IsDirectory == true))
                {
                    // Start async loading without blocking
                    _ = LoadChildrenAsync();
                }

                return _children;
            }
        }

        private async Task LoadChildrenAsync()
        {
            CancellationToken cancellationToken;
            int loadGeneration;
            bool restartLoad = false;

            lock (_loadLock)
            {
                if (_isLoading || _isLoaded || IsDisposed)
                {
                    return;
                }

                _isLoading = true;
                loadGeneration = _loadGeneration;
                _loadCancellationTokenSource?.Cancel();
                _loadCancellationTokenSource = new CancellationTokenSource();
                cancellationToken = _loadCancellationTokenSource.Token;
            }

            try
            {
                // Do the heavy work off the UI thread
                List<VsixItemNode> childNodes = await Task.Run(() => LoadChildrenOffUIThread(cancellationToken), cancellationToken);

                if (cancellationToken.IsCancellationRequested || IsDisposed || !IsCurrentLoad(loadGeneration))
                {
                    DisposeChildren(newChildren: childNodes);
                    return;
                }

                // Switch back to UI thread to update the collection
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                if (cancellationToken.IsCancellationRequested || IsDisposed || !IsCurrentLoad(loadGeneration))
                {
                    DisposeChildren(newChildren: childNodes);
                    return;
                }

                UpdateChildrenOnUIThread(childNodes);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
            }
            catch (Exception ex)
            {
                // Log error but don't crash
                ex.Log();
            }
            finally
            {
                lock (_loadLock)
                {
                    _isLoading = false;

                    if (_reloadRequested && !IsDisposed)
                    {
                        _reloadRequested = false;
                        restartLoad = true;
                    }
                }

                if (restartLoad)
                {
                    _ = LoadChildrenAsync();
                }
            }
        }

        private bool IsCurrentLoad(int loadGeneration)
        {
            lock (_loadLock)
            {
                return loadGeneration == _loadGeneration;
            }
        }

        private static void DisposeChildren(IEnumerable<VsixItemNode> newChildren)
        {
            foreach (VsixItemNode child in newChildren)
            {
                child.Dispose();
            }
        }

        private List<VsixItemNode> LoadChildrenOffUIThread(CancellationToken cancellationToken)
        {
            var activeNodes = new List<VsixItemNode>();

            if (_archiveEntry?.IsDirectory == true)
            {
                if (IsArchiveRoot && RootNode?.Comparison?.HasChanges == true)
                {
                    activeNodes.Add(CreateComparisonNode(this, RootNode.Comparison));
                }

                foreach (VsixArchiveEntry entry in _archiveEntry.Children)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    activeNodes.Add(new VsixItemNode(this, entry));
                }
            }
            else if (Info is DirectoryInfo directory)
            {
                try
                {
                    foreach (FileSystemInfo item in directory.EnumerateFileSystemInfos())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (IsInternalMetadataFile(item))
                        {
                            continue;
                        }

                        var child = new VsixItemNode(this, item.FullName, null);
                        activeNodes.Add(child);
                    }

                    // Sort off UI thread
                    activeNodes.Sort((lhs, rhs) =>
                    {
                        // Optimized comparison - check directory first without GetType()
                        var lhsIsDir = lhs.Info is DirectoryInfo;
                        var rhsIsDir = rhs.Info is DirectoryInfo;

                        return lhsIsDir == rhsIsDir
                            ? _stringComparer.Compare(lhs.Text, rhs.Text)
                            : lhsIsDir ? -1 : 1;
                    });
                }
                catch (UnauthorizedAccessException)
                {
                    // Directory not accessible
                }
                catch (DirectoryNotFoundException)
                {
                    // Directory was deleted
                }
            }

            return activeNodes;
        }

        private static bool IsInternalMetadataFile(FileSystemInfo item)
        {
            return item is FileInfo && string.Equals(item.Name, ".vsixstamp", StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateChildrenOnUIThread(List<VsixItemNode> newChildren)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _children ??= [];

            if (AreChildrenEquivalent(_children, newChildren))
            {
                foreach (VsixItemNode item in newChildren)
                {
                    item.Dispose();
                }

                _isLoaded = true;

                bool hadItems = HasItems;
                HasItems = _children.Any();
                if (hadItems != HasItems)
                {
                    RaisePropertyChanged(nameof(HasItems));
                }

                return;
            }

            // Dispose old children
            foreach (VsixItemNode child in _children)
            {
                child.Dispose();
            }

            _children.BeginBulkOperation();
            _children.Clear();
            _children.AddRange(newChildren);
            _children.EndBulkOperation();

            _isLoaded = true;
            HasItems = _children.Any();

            RaisePropertyChanged(nameof(Items));
            RaisePropertyChanged(nameof(HasItems));
        }

        private static bool AreChildrenEquivalent(IList<VsixItemNode> existingChildren, IList<VsixItemNode> newChildren)
        {
            if (existingChildren.Count != newChildren.Count)
            {
                return false;
            }

            for (int i = 0; i < existingChildren.Count; i++)
            {
                string existingPath = existingChildren[i].NodeIdentity;
                string newPath = newChildren[i].NodeIdentity;

                if (!string.Equals(existingPath, newPath, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        public string Text { get; set; }
        private string NodePath => _virtualPath ?? _archiveEntry?.FullName ?? Info?.FullName;
        private string NodeIdentity => _archiveEntry?.Identity ?? NodePath;
        public string ToolTipText => null;
        public string StateToolTipText => null;
        public object ToolTipContent { get; set; }

        public FontWeight FontWeight => FontWeights.Normal;
        public System.Windows.FontStyle FontStyle => FontStyles.Normal;

        public ImageMoniker IconMoniker => GetIcon(isOpen: false);
        public ImageMoniker ExpandedIconMoniker => GetIcon(isOpen: true);
        public ImageMoniker OverlayIconMoniker => default;
        public ImageMoniker StateIconMoniker => default;

        public int Priority => 0;
        public bool IsCut { get; set; }
        public bool IsDisposed
        {
            get;
            set
            {
                if (field != value)
                {
                    field = value;
                    RaisePropertyChanged(nameof(IsDisposed));
                }
            }
        }
        public bool CanPreview => _virtualPath == null && (Info is FileInfo || _archiveEntry?.IsDirectory == false);

        public IInvocationController InvocationController => VsixItemInvocationController.Instance;
        public IContextMenuController ContextMenuController => VsixContextMenuController.Instance;

        internal bool IsArchiveRoot => _archiveEntry != null && string.IsNullOrEmpty(_archiveEntry.FullName);
        internal string PackagePath => IsArchiveRoot ? RootNode?.VsixPath : _archiveEntry?.FullName;
        internal VsixArchive Archive => _archiveEntry?.Owner;
        internal VsixRootNode RootNode
        {
            get
            {
                object source = SourceItem;

                while (source is VsixItemNode node)
                {
                    source = node.SourceItem;
                }

                return source as VsixRootNode;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Refresh()
        {
            // Reset loading state and trigger async reload
            lock (_loadLock)
            {
                _isLoaded = false;
                _loadGeneration++;

                if (_isLoading)
                {
                    _reloadRequested = true;
                }

                _loadCancellationTokenSource?.Cancel();
            }

            // Trigger async loading
            _ = LoadChildrenAsync();
        }

        public async Task RefreshAsync()
        {
            // Reset loading state
            lock (_loadLock)
            {
                _isLoaded = false;
                _loadGeneration++;

                if (_isLoading)
                {
                    _reloadRequested = true;
                }

                _loadCancellationTokenSource?.Cancel();
            }

            // Await the loading to complete
            await LoadChildrenAsync();
        }

        public void CancelLoad()
        {
            lock (_loadLock)
            {
                _loadGeneration++;
            }

            _loadCancellationTokenSource?.Cancel();
        }

        public int CompareTo(object obj)
        {
            if (obj is not VsixItemNode node)
            {
                return obj is ITreeDisplayItem item ? _stringComparer.Compare(Text, item.Text) : 0;
            }

            bool thisIsDirectory = IsDirectory;
            bool otherIsDirectory = node.IsDirectory;

            if (thisIsDirectory != otherIsDirectory)
            {
                return thisIsDirectory ? -1 : 1;
            }

            return _stringComparer.Compare(Text, node.Text);
        }

        internal bool IsDirectory => _virtualPath != null ? _isVirtualDirectory : _archiveEntry?.IsDirectory ?? Info is DirectoryInfo;

        internal string GetOpenPath()
        {
            if (_archiveEntry?.IsDirectory == false)
            {
                return _archiveEntry.Owner.Materialize(_archiveEntry);
            }

            return (Info as FileInfo)?.FullName;
        }

        private ImageMoniker GetIcon(bool isOpen)
        {
            if (_showErrorIcon)
            {
                return KnownMonikers.StatusError;
            }

            if (_showVsixIcon)
            {
                return KnownMonikers.Extension;
            }

            if (_virtualPath != null)
            {
                return _isVirtualDirectory
                    ? isOpen ? KnownMonikers.FolderOpened : KnownMonikers.FolderClosed
                    : KnownMonikers.StatusInformation;
            }

            if (_archiveEntry != null)
            {
                return IconMapper.GetIcon(_archiveEntry.Name, _archiveEntry.IsDirectory, isOpen);
            }

            return Info.GetIcon(isOpen);
        }

        private string SetTooltip(string vsixFile)
        {
            if (Info == null)
            {
                return "Compile to see content of generated .vsix file";
            }
            else if (!string.IsNullOrEmpty(vsixFile) && File.Exists(vsixFile))
            {
                // Avoid expensive file size lookup on UI thread - just show basic info
                // File size could be computed async if needed for tooltip
                return $"Last updated: {Info.LastWriteTime}\r\nVSIX file: {Path.GetFileName(vsixFile)}";
            }

            return $"Last updated: {Info.LastWriteTime}";
        }

        private static string SetArchiveTooltip(VsixArchiveEntry entry)
        {
            if (entry.IsDirectory)
            {
                return $"{entry.Children.Count:N0} item(s)\r\nPackage: {Path.GetFileName(entry.Owner.OriginalPath)}\r\nPackage path: {entry.FullName}";
            }

            return $"Size: {entry.Length:N0} bytes\r\nPackage: {Path.GetFileName(entry.Owner.OriginalPath)}\r\nPackage path: {entry.FullName}\r\nOpens as a read-only temporary copy.";
        }

        private static VsixItemNode CreateComparisonNode(IAttachedCollectionSource source, VsixArchiveComparison comparison)
        {
            var root = new VsixItemNode(
                source,
                "Changes from previous build",
                $"+{comparison.Added.Count:N0} added, -{comparison.Removed.Count:N0} removed, ~{comparison.Changed.Count:N0} changed",
                virtualPath: "comparison",
                isDirectory: true);

            var categories = new List<VsixItemNode>();
            AddComparisonCategory(root, categories, "Added", comparison.Added);
            AddComparisonCategory(root, categories, "Removed", comparison.Removed);
            AddComparisonCategory(root, categories, "Changed", comparison.Changed);
            root._virtualChildren = categories;
            root.HasItems = categories.Count > 0;
            return root;
        }

        private static void AddComparisonCategory(
            VsixItemNode root,
            ICollection<VsixItemNode> categories,
            string category,
            IReadOnlyList<string> paths)
        {
            if (paths.Count == 0)
            {
                return;
            }

            var categoryNode = new VsixItemNode(
                root,
                $"{category} ({paths.Count:N0})",
                $"{paths.Count:N0} package entries {category.ToLowerInvariant()} since the previous build.",
                virtualPath: "comparison/" + category,
                isDirectory: true);

            categoryNode._virtualChildren = paths
                .Select(path => new VsixItemNode(categoryNode, path, path, "comparison/" + category + "/" + path, isDirectory: false))
                .ToArray();
            categoryNode.HasItems = true;
            categories.Add(categoryNode);
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;

            // Cancel any ongoing loading
            _loadCancellationTokenSource?.Cancel();
            _loadCancellationTokenSource?.Dispose();
            _loadCancellationTokenSource = null;

            // Dispose children efficiently
            if (_children != null)
            {
                // Create local copy to avoid issues during disposal
                VsixItemNode[] childrenToDispose = [.. _children];
                _children.Clear();
                _children = null;

                foreach (VsixItemNode item in childrenToDispose)
                {
                    item.Dispose();
                }
            }

            // Clear event handlers to help GC
            PropertyChanged = null;
        }

        public object GetBrowseObject() => this;

        public TPattern GetPattern<TPattern>() where TPattern : class
        {
            if (!IsDisposed)
            {
                // Optimized pattern lookup using type comparison instead of HashSet
                Type patternType = typeof(TPattern);

                if (patternType == typeof(ITreeDisplayItem) ||
                    patternType == typeof(IBrowsablePattern) ||
                    patternType == typeof(IInvocationPattern) ||
                    patternType == typeof(ISupportDisposalNotification) ||
                    patternType == typeof(IRefreshPattern))
                {
                    return this as TPattern;
                }

                if (patternType == typeof(IContextMenuPattern) && _archiveEntry != null)
                {
                    return this as TPattern;
                }
            }
            else
            {
                // If this item has been deleted, it no longer supports any patterns
                // other than ISupportDisposalNotification.
                if (typeof(TPattern) == typeof(ISupportDisposalNotification))
                {
                    return this as TPattern;
                }
            }

            return null;
        }

        public void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
