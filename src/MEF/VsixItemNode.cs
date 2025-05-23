using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Internal.VisualStudio.PlatformUI;
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
        //IContextMenuPattern,
        IInvocationPattern,
        ISupportDisposalNotification,
        IDisposable,
        IRefreshPattern
    {

        private static readonly HashSet<Type> _supportedPatterns =
        [
            typeof(ITreeDisplayItem),
            typeof(IBrowsablePattern),
            //typeof(IContextMenuPattern),
            typeof(IInvocationPattern),
            typeof(ISupportDisposalNotification),
            typeof(IRefreshPattern),
        ];

        private BulkObservableCollection<VsixItemNode> _children;
        private bool _isDisposed;

        public VsixItemNode(IAttachedCollectionSource source, string outputPath, string vsixPath)
        {
            SourceItem = source;
            Rebuild(outputPath, vsixPath);
        }

        public void Rebuild(string outputPath, string vsixPath)
        {
            Text = Path.GetFileName(outputPath) ?? ".vsix content";
            IsCut = false;

            if (Directory.Exists(outputPath))
            {
                Info = new DirectoryInfo(outputPath);
                HasItems = true;
                RaisePropertyChanged(nameof(HasItems));
            }
            else if (File.Exists(outputPath))
            {
                Info = new FileInfo(outputPath);
            }
            else
            {
                IsCut = true;
            }

            RaisePropertyChanged(nameof(Text));
            RaisePropertyChanged(nameof(IsCut));

            if (!string.IsNullOrEmpty(vsixPath))
            {
                ToolTipContent = SetTooltip(vsixPath);
                RaisePropertyChanged(nameof(ToolTipContent));
            }
        }

        public FileSystemInfo Info { get; set; }
        public object SourceItem { get; init; }
        public bool HasItems { get; set; }
        public IEnumerable Items
        {
            get
            {
                if (_children == null)
                {
                    Refresh();
                }

                return _children;
            }
        }

        public string Text { get; set; }
        public string ToolTipText => null;
        public string StateToolTipText => null;
        public object ToolTipContent { get; set; }

        public FontWeight FontWeight => FontWeights.Normal;
        public System.Windows.FontStyle FontStyle => FontStyles.Normal;


        public ImageMoniker IconMoniker => Info.GetIcon(false);
        public ImageMoniker ExpandedIconMoniker => Info.GetIcon(true);
        public ImageMoniker OverlayIconMoniker => default;
        public ImageMoniker StateIconMoniker => default;

        public int Priority => 0;
        public bool IsCut { get; set; }
        public bool IsDisposed
        {
            get => _isDisposed;
            set
            {
                if (_isDisposed != value)
                {
                    _isDisposed = value;
                    RaisePropertyChanged(nameof(IsDisposed));
                }
            }
        }
        public bool CanPreview => true;

        public IInvocationController InvocationController => new VsixItemInvocationController();

        public event PropertyChangedEventHandler PropertyChanged;

        private void Refresh()
        {
            _children ??= [];

            foreach (VsixItemNode child in _children)
            {
                child.Dispose();
            }

            List<VsixItemNode> activeNodes = [];

            if (Info is DirectoryInfo directory)
            {
                foreach (FileSystemInfo item in directory.EnumerateFileSystemInfos())
                {
                    var child = new VsixItemNode(this, item.FullName, null);
                    activeNodes.Add(child);
                }
            }

            activeNodes.Sort((lhs, rhs) =>
            {
                return lhs.Info.GetType() == rhs.Info.GetType()
                    ? StringComparer.OrdinalIgnoreCase.Compare(lhs.Text, rhs.Text)
                    : lhs.Info is DirectoryInfo ? -1 : 1;
            });

            _children.BeginBulkOperation();
            _children.Clear();
            _children.AddRange(activeNodes);
            _children.EndBulkOperation();
            HasItems = _children.Any();

            RaisePropertyChanged(nameof(Items));
            RaisePropertyChanged(nameof(HasItems));
        }

        public Task RefreshAsync()
        {
            Refresh();
            return Task.CompletedTask;
        }

        public void CancelLoad()
        {
        }

        public int CompareTo(object obj)
        {
            return obj is ITreeDisplayItem item ? StringComparer.OrdinalIgnoreCase.Compare(Text, item.Text) : 0;
        }

        private string SetTooltip(string vsixFile)
        {
            if (Info == null)
            {
                return "Compile to see content of generated .vsix file";
            }
            else if (!string.IsNullOrEmpty(vsixFile) && File.Exists(vsixFile))
            {
                return $"Last updated: {Info.LastWriteTime}\r\nFile size: {new FileInfo(vsixFile).Length} bytes";
            }

            return $"Last updated: {Info.LastWriteTime}";
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;


            if (_children != null)
            {
                foreach (VsixItemNode item in _children)
                {
                    item.Dispose();
                }
            }
        }

        public object GetBrowseObject() => this;

        public TPattern GetPattern<TPattern>() where TPattern : class
        {
            if (!IsDisposed)
            {
                if (_supportedPatterns.Contains(typeof(TPattern)))
                {
                    return this as TPattern;
                }
            }
            else
            {
                // If this item has been deleted, it no longer supports any patterns
                // other than ISupportDisposalNotification.
                // It's valid to use GetPattern on a deleted item, but there are no
                // longer any pattern contracts it fulfills other than the contract
                // that reports the item as a dead ITransientObject.
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
