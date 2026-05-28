// ============================================================
// MainForm.cs – Dark-themed disk inspection UI
// Tabs: Search | File Browser | Disks & Partitions | NTFS | Hex Viewer
// ============================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DiskInspector.Core;
using DiskInspector.Models;

namespace DiskInspector.UI
{
    public class MainForm : Form
    {
        // ---- Dark theme palette ----
        static readonly Color BG        = Color.FromArgb(18, 18, 18);
        static readonly Color BG2       = Color.FromArgb(28, 28, 28);
        static readonly Color BG3       = Color.FromArgb(42, 42, 46);
        static readonly Color ACCENT    = Color.FromArgb(0, 120, 215);
        static readonly Color ACCENT2   = Color.FromArgb(0, 180, 120);
        static readonly Color WARN      = Color.FromArgb(255, 160, 0);
        static readonly Color FG        = Color.FromArgb(220, 220, 220);
        static readonly Color FG_DIM    = Color.FromArgb(130, 130, 130);
        static readonly Color RED       = Color.FromArgb(220, 60, 60);
        static readonly Color GREEN     = Color.FromArgb(80, 200, 100);
        static readonly Color YELLOW    = Color.FromArgb(220, 200, 60);
        static readonly Color GRID_LINE = Color.FromArgb(50, 50, 55);

        static readonly Font FONT_MAIN = new("Segoe UI", 10f);
        static readonly Font FONT_MONO = new("Consolas", 10f);
        static readonly Font FONT_BOLD = new("Segoe UI", 10f, FontStyle.Bold);
        static readonly Font FONT_H1   = new("Segoe UI", 15f, FontStyle.Bold);
        static readonly Font FONT_H2   = new("Segoe UI", 11f, FontStyle.Bold);

        // Row-height hack for ListViews — 24px tall rows
        static readonly ImageList ROW_HEIGHT = new() { ImageSize = new Size(1, 24), ColorDepth = ColorDepth.Depth32Bit };

        // ---- Controls ----
        TabControl   _tabs        = null!;
        StatusStrip  _status      = null!;
        ToolStripStatusLabel _statusLabel = null!;
        ToolStripStatusLabel _indexLabel  = null!;
        ToolStripStatusLabel _elevLabel   = null!;

        // Search tab
        TextBox  _searchBox  = null!;
        ListView _searchList = null!;
        CheckBox _chkHidden  = null!;
        CheckBox _chkSystem  = null!;
        Label    _searchInfo = null!;
        Button   _btnReIndex = null!;

        // File Browser tab
        TreeView      _fsTree    = null!;
        ListView      _fsList    = null!;
        TextBox       _pathBox   = null!;
        RichTextBox   _fileDetail= null!;
        SplitContainer _fsSplit  = null!;
        SplitContainer _fsRight  = null!;

        // Disk tab
        ListView      _diskList   = null!;
        ListView      _partList   = null!;
        RichTextBox   _diskDetail = null!;
        SplitContainer _diskSplit = null!;

        // NTFS tab
        ComboBox   _ntfsVolCombo = null!;
        ListView   _mftList      = null!;
        RichTextBox _mftDetail   = null!;
        Button     _btnReadMft   = null!;
        Button     _btnMftAll    = null!;
        ProgressBar _mftProgress = null!;
        Label      _mftStatus    = null!;
        SplitContainer _mftSplit = null!;

        // Hex tab
        TextBox       _hexPathBox     = null!;
        RichTextBox   _hexView        = null!;
        NumericUpDown _hexOffset      = null!;
        NumericUpDown _hexLength      = null!;
        Button        _btnHexRead     = null!;
        Button        _btnHexBrowse   = null!;
        ComboBox      _hexTargetCombo = null!;

        List<PhysicalDisk> _disks = new();

        public MainForm()
        {
            InitializeUI();
            Load += OnLoad;
        }

        private void InitializeUI()
        {
            Text            = "DiskInspector  -  Advanced Filesystem & Disk Analysis";
            Size            = new Size(1400, 900);
            MinimumSize     = new Size(1000, 650);
            StartPosition   = FormStartPosition.CenterScreen;
            BackColor       = BG;
            ForeColor       = FG;
            Font            = FONT_MAIN;
            Icon            = SystemIcons.Shield;

            // Status bar
            _status      = new StatusStrip { BackColor = BG2, ForeColor = FG };
            _statusLabel = new ToolStripStatusLabel("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _indexLabel  = new ToolStripStatusLabel("Index: not built") { ForeColor = FG_DIM };
            _elevLabel   = new ToolStripStatusLabel("") { Font = FONT_BOLD };
            _status.Items.AddRange(new ToolStripItem[] { _statusLabel, _indexLabel, _elevLabel });
            Controls.Add(_status);

            // Tab control
            _tabs = new TabControl
            {
                Dock     = DockStyle.Fill,
                DrawMode = TabDrawMode.OwnerDrawFixed,
                ItemSize = new Size(170, 36),
                SizeMode = TabSizeMode.Fixed,
                Padding  = new Point(0, 0),
                BackColor = BG,
                ForeColor = FG,
            };
            _tabs.DrawItem += DrawTab;
            Controls.Add(_tabs);

            BuildSearchTab();
            BuildFsBrowserTab();
            BuildDiskTab();
            BuildNtfsTab();
            BuildHexTab();

            _searchBox.TextChanged    += (s, e) => DoSearch();
            _chkHidden.CheckedChanged += (s, e) => DoSearch();
            _chkSystem.CheckedChanged += (s, e) => DoSearch();
        }

        private void DrawTab(object? sender, DrawItemEventArgs e)
        {
            var tabs     = (TabControl)sender!;
            var page     = tabs.TabPages[e.Index];
            bool selected = e.Index == tabs.SelectedIndex;

            using var bgBrush    = new SolidBrush(selected ? BG3 : BG2);
            using var fgBrush    = new SolidBrush(selected ? FG : FG_DIM);
            using var accentPen  = new Pen(ACCENT, 2);

            e.Graphics.FillRectangle(bgBrush, e.Bounds);
            if (selected)
                e.Graphics.DrawLine(accentPen, e.Bounds.Left, e.Bounds.Top, e.Bounds.Right, e.Bounds.Top);

            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString(page.Text, selected ? FONT_BOLD : FONT_MAIN, fgBrush, e.Bounds, sf);
        }

        // ================================================================
        //  SEARCH TAB
        // ================================================================
        private void BuildSearchTab()
        {
            var page = MakePage("Search");

            // Toolbar using FlowLayoutPanel so nothing clips
            var flow = new FlowLayoutPanel
            {
                Dock          = DockStyle.Top,
                Height        = 52,
                BackColor     = BG2,
                Padding       = new Padding(10, 10, 10, 6),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = false,
                AutoSize      = false,
            };

            var lblSearch = MakeFlowLabel("Search:");
            _searchBox = new TextBox
            {
                Width = 560, Height = 28,
                BackColor = BG3, ForeColor = FG,
                BorderStyle = BorderStyle.FixedSingle, Font = FONT_MAIN,
                PlaceholderText = "name, *.ext, path:C:\\Windows, size>100MB, ext:dll ...",
                Margin = new Padding(4, 2, 8, 0),
            };

            _chkHidden = MakeFlowCheck("Hidden");
            _chkHidden.Checked = true;
            _chkSystem = MakeFlowCheck("System");
            _chkSystem.Checked = true;

            _btnReIndex = MakeFlowButton("Re-Index", ACCENT, 100);
            _btnReIndex.Click += (s, e) => StartIndex();

            _searchInfo = new Label
            {
                AutoSize = true, ForeColor = FG_DIM, BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(16, 6, 0, 0),
                Font = FONT_MAIN,
            };

            flow.Controls.AddRange(new Control[] { lblSearch, _searchBox, _chkHidden, _chkSystem, _btnReIndex, _searchInfo });

            // Results list
            _searchList = MakeListView();
            _searchList.Dock = DockStyle.Fill;
            _searchList.DoubleClick += SearchList_DoubleClick;
            _searchList.ContextMenuStrip = MakeSearchContextMenu();
            _searchList.Columns.AddRange(new[]
            {
                new ColumnHeader { Text = "Name",      Width = 300 },
                new ColumnHeader { Text = "Full Path",  Width = 580 },
                new ColumnHeader { Text = "Size",       Width = 100, TextAlign = HorizontalAlignment.Right },
                new ColumnHeader { Text = "Modified",   Width = 155 },
                new ColumnHeader { Text = "Attrs",      Width = 65  },
                new ColumnHeader { Text = "Type",       Width = 80  },
            });

            page.Controls.Add(_searchList);
            page.Controls.Add(flow);
        }

        private ContextMenuStrip MakeSearchContextMenu()
        {
            var cms = new ContextMenuStrip { BackColor = BG2, ForeColor = FG };
            var copyPath = new ToolStripMenuItem("Copy full path");
            copyPath.Click += (s, e) =>
            {
                if (_searchList.SelectedItems.Count > 0)
                    Clipboard.SetText(_searchList.SelectedItems[0].SubItems[1].Text);
            };
            var openLoc = new ToolStripMenuItem("Open containing folder");
            openLoc.Click += (s, e) =>
            {
                if (_searchList.SelectedItems.Count > 0)
                {
                    string path = _searchList.SelectedItems[0].SubItems[1].Text;
                    string? dir = Path.GetDirectoryName(path);
                    if (dir != null) System.Diagnostics.Process.Start("explorer.exe", dir);
                }
            };
            var navigateTo = new ToolStripMenuItem("Navigate to in Browser");
            navigateTo.Click += (s, e) =>
            {
                if (_searchList.SelectedItems.Count > 0)
                {
                    string path = _searchList.SelectedItems[0].SubItems[1].Text;
                    _tabs.SelectedIndex = 1;
                    NavigateTo(path);
                }
            };
            cms.Items.AddRange(new ToolStripItem[] { copyPath, openLoc, navigateTo });
            return cms;
        }

        private void DoSearch()
        {
            string q = _searchBox.Text;
            var results = FileSystemBrowser.Search(q, 10000, false);

            bool showHidden = _chkHidden.Checked;
            bool showSystem = _chkSystem.Checked;

            if (!showHidden) results = results.Where(r => (r.Attributes & FileAttributes.Hidden) == 0).ToList();
            if (!showSystem) results = results.Where(r => (r.Attributes & FileAttributes.System) == 0).ToList();

            _searchList.BeginUpdate();
            _searchList.Items.Clear();

            foreach (var r in results.Take(10000))
            {
                var item = new ListViewItem(r.Name)
                {
                    Tag = r.FullPath,
                    ForeColor = r.IsDirectory ? ACCENT
                              : r.IsHidden    ? FG_DIM
                              : (r.Attributes & FileAttributes.System) != 0 ? WARN
                              : FG,
                };
                item.SubItems.Add(r.FullPath);
                item.SubItems.Add(r.IsDirectory ? "" : DiskEnumerator.FormatSize((ulong)Math.Max(0, r.Size)));
                item.SubItems.Add(r.LastWriteTime == DateTime.MinValue ? "" : r.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                item.SubItems.Add(r.AttributeString);
                item.SubItems.Add(r.IsDirectory ? "Folder" : (r.Extension.Length > 0 ? r.Extension.ToUpperInvariant() : "File"));
                _searchList.Items.Add(item);
            }

            _searchList.EndUpdate();
            _searchInfo.Text = $"{results.Count:N0} results  /  {FileSystemBrowser.IndexedCount:N0} indexed";
        }

        private void SearchList_DoubleClick(object? sender, EventArgs e)
        {
            if (_searchList.SelectedItems.Count == 0) return;
            string path = _searchList.SelectedItems[0].SubItems[1].Text;
            _tabs.SelectedIndex = 1;
            NavigateTo(path);
        }

        // ================================================================
        //  FILE BROWSER TAB
        // ================================================================
        private void BuildFsBrowserTab()
        {
            var page = MakePage("File Browser");

            // Path bar
            var pathFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 50, BackColor = BG2,
                Padding = new Padding(10, 10, 10, 6),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false, AutoSize = false,
            };
            var lblPath = MakeFlowLabel("Path:");
            _pathBox = new TextBox
            {
                Width = 980, Height = 28, BackColor = BG3, ForeColor = FG,
                BorderStyle = BorderStyle.FixedSingle, Font = FONT_MAIN,
                Margin = new Padding(4, 2, 6, 0),
            };
            _pathBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) NavigateTo(_pathBox.Text); };
            var btnGo = MakeFlowButton("Go", ACCENT, 52);
            btnGo.Click += (s, e) => NavigateTo(_pathBox.Text);
            var btnUp = MakeFlowButton("Up", BG3, 52);
            btnUp.Click += (s, e) => NavigateUp();
            pathFlow.Controls.AddRange(new Control[] { lblPath, _pathBox, btnGo, btnUp });

            // Main split
            _fsSplit = new SplitContainer { Dock = DockStyle.Fill, BackColor = BG, SplitterWidth = 5 };
            _fsSplit.SplitterDistance = 300;
            _fsSplit.Panel1.BackColor = BG;
            _fsSplit.Panel2.BackColor = BG;

            _fsTree = new TreeView
            {
                Dock = DockStyle.Fill, BackColor = BG2, ForeColor = FG,
                BorderStyle = BorderStyle.None, Font = FONT_MAIN,
                ShowLines = true, LineColor = GRID_LINE, HideSelection = false,
            };
            _fsTree.AfterSelect   += FsTree_AfterSelect;
            _fsTree.BeforeExpand  += FsTree_BeforeExpand;
            _fsSplit.Panel1.Controls.Add(_fsTree);

            _fsRight = new SplitContainer
            {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                BackColor = BG, SplitterWidth = 5,
            };
            _fsRight.SplitterDistance = 500;

            _fsList = MakeListView();
            _fsList.Dock = DockStyle.Fill;
            _fsList.DoubleClick += FsList_DoubleClick;
            _fsList.SelectedIndexChanged += FsList_SelectionChanged;
            _fsList.Columns.AddRange(new[]
            {
                new ColumnHeader { Text = "Name",        Width = 320 },
                new ColumnHeader { Text = "Size",        Width = 100, TextAlign = HorizontalAlignment.Right },
                new ColumnHeader { Text = "Modified",    Width = 155 },
                new ColumnHeader { Text = "Created",     Width = 155 },
                new ColumnHeader { Text = "Attrs",       Width = 90  },
                new ColumnHeader { Text = "Type",        Width = 70  },
                new ColumnHeader { Text = "Link Target", Width = 220 },
            });
            _fsRight.Panel1.Controls.Add(_fsList);

            _fileDetail = MakeRichText();
            _fileDetail.Dock = DockStyle.Fill;
            _fileDetail.Font = FONT_MONO;
            _fileDetail.ReadOnly = true;
            _fsRight.Panel2.Controls.Add(_fileDetail);

            _fsSplit.Panel2.Controls.Add(_fsRight);
            page.Controls.Add(_fsSplit);
            page.Controls.Add(pathFlow);
        }

        private void FsTree_AfterSelect(object? sender, TreeViewEventArgs e)
        {
            if (e.Node?.Tag is string path) LoadDirectory(path);
        }

        private void FsTree_BeforeExpand(object? sender, TreeViewCancelEventArgs e)
        {
            if (e.Node == null) return;
            if (e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Tag?.ToString() == "DUMMY")
            {
                e.Node.Nodes.Clear();
                if (e.Node.Tag is string path) PopulateTreeNode(e.Node, path);
            }
        }

        private void PopulateTreeNode(TreeNode parent, string path)
        {
            try
            {
                var opts = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true };
                foreach (string dir in Directory.EnumerateDirectories(path, "*", opts))
                {
                    var di   = new DirectoryInfo(dir);
                    var node = new TreeNode(di.Name) { Tag = di.FullName };
                    node.ForeColor = (di.Attributes & FileAttributes.Hidden) != 0 ? FG_DIM
                                   : (di.Attributes & FileAttributes.System)  != 0 ? WARN : FG;
                    try
                    {
                        if (Directory.EnumerateDirectories(dir, "*",
                            new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true }).Any())
                            node.Nodes.Add(new TreeNode("...") { Tag = "DUMMY" });
                    }
                    catch { }
                    parent.Nodes.Add(node);
                }
            }
            catch { }
        }

        private void FsList_DoubleClick(object? sender, EventArgs e)
        {
            if (_fsList.SelectedItems.Count == 0) return;
            if (_fsList.SelectedItems[0].Tag is FileSystemEntry entry && entry.IsDirectory)
            {
                LoadDirectory(entry.FullPath);
                ExpandTreeTo(entry.FullPath);
            }
        }

        private void FsList_SelectionChanged(object? sender, EventArgs e)
        {
            if (_fsList.SelectedItems.Count == 0) { _fileDetail.Clear(); return; }
            if (_fsList.SelectedItems[0].Tag is FileSystemEntry entry) ShowFileDetail(entry);
        }

        private void ShowFileDetail(FileSystemEntry entry)
        {
            _fileDetail.Clear();
            _fileDetail.BackColor = BG2;
            AppendLine(_fileDetail, "  FILE / DIRECTORY DETAILS", FG, FONT_H2, BG3);
            AppendLine(_fileDetail, "", FG, FONT_MAIN);
            AppendPair(_fileDetail, "  Name",      entry.Name);
            AppendPair(_fileDetail, "  Full Path", entry.FullPath);
            AppendPair(_fileDetail, "  Type",      entry.IsDirectory ? "Directory" : "File");
            if (!entry.IsDirectory && entry.Size >= 0)
                AppendPair(_fileDetail, "  Size", $"{DiskEnumerator.FormatSize((ulong)entry.Size)}  ({entry.Size:N0} bytes)");
            AppendPair(_fileDetail, "  Attributes", entry.AttributeString);
            AppendPair(_fileDetail, "  Created",    FmtTime(entry.CreationTime));
            AppendPair(_fileDetail, "  Modified",   FmtTime(entry.LastWriteTime));
            AppendPair(_fileDetail, "  Accessed",   FmtTime(entry.LastAccessTime));
            if (entry.IsSymLink)  AppendPair(_fileDetail, "  Symlink ->",  entry.LinkTarget ?? "(unresolved)");
            if (entry.IsJunction) AppendPair(_fileDetail, "  Junction ->", entry.LinkTarget ?? "(unresolved)");
            AppendLine(_fileDetail, "", FG, FONT_MAIN);
            var flags = new List<(string label, bool set, Color col)>
            {
                ("HIDDEN",    entry.IsHidden,    WARN),
                ("SYSTEM",    entry.IsSystem,    RED),
                ("READONLY",  (entry.Attributes & FileAttributes.ReadOnly)   != 0, FG_DIM),
                ("ARCHIVE",   (entry.Attributes & FileAttributes.Archive)    != 0, FG_DIM),
                ("COMPRESSED",(entry.Attributes & FileAttributes.Compressed) != 0, ACCENT2),
                ("ENCRYPTED", (entry.Attributes & FileAttributes.Encrypted)  != 0, YELLOW),
                ("REPARSE",   entry.IsReparsePoint, ACCENT),
                ("SPARSE",    (entry.Attributes & FileAttributes.SparseFile) != 0, FG_DIM),
                ("TEMP",      (entry.Attributes & FileAttributes.Temporary)  != 0, FG_DIM),
                ("OFFLINE",   (entry.Attributes & FileAttributes.Offline)    != 0, FG_DIM),
            };
            _fileDetail.SelectionIndent = 10;
            foreach (var (label, set, col) in flags)
            {
                _fileDetail.SelectionColor = set ? col : GRID_LINE;
                _fileDetail.AppendText($"[{(set ? "o" : ".")} {label}]  ");
            }
            _fileDetail.AppendText("\n");
        }

        // ================================================================
        //  DISK TAB
        // ================================================================
        private void BuildDiskTab()
        {
            var page = MakePage("Disks & Partitions");

            var refreshBtn = MakeFlowButton("Refresh Disks", ACCENT, 140);
            refreshBtn.Dock = DockStyle.Top;
            refreshBtn.Height = 34;
            refreshBtn.Click += (s, e) => LoadDisks();

            _diskSplit = new SplitContainer { Dock = DockStyle.Fill, BackColor = BG, SplitterWidth = 5 };
            _diskSplit.SplitterDistance = 340;

            var lblDisks = new Label { Text = "Physical Disks", Dock = DockStyle.Top, Height = 26, Font = FONT_BOLD, ForeColor = FG, BackColor = BG2, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6,0,0,0) };
            _diskList = MakeListView();
            _diskList.Dock = DockStyle.Fill;
            _diskList.Columns.AddRange(new[]
            {
                new ColumnHeader { Text = "Disk",  Width = 50  },
                new ColumnHeader { Text = "Model", Width = 200 },
                new ColumnHeader { Text = "Size",  Width = 90  },
                new ColumnHeader { Text = "Style", Width = 60  },
            });
            _diskList.SelectedIndexChanged += DiskList_SelChanged;

            var lblParts = new Label { Text = "Partitions", Dock = DockStyle.Top, Height = 26, Font = FONT_BOLD, ForeColor = FG, BackColor = BG2, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6,0,0,0) };
            _partList = MakeListView();
            _partList.Dock = DockStyle.Fill;
            _partList.Columns.AddRange(new[]
            {
                new ColumnHeader { Text = "#",      Width = 36  },
                new ColumnHeader { Text = "Letter", Width = 60  },
                new ColumnHeader { Text = "Type",   Width = 140 },
                new ColumnHeader { Text = "Size",   Width = 90  },
                new ColumnHeader { Text = "Offset", Width = 100 },
                new ColumnHeader { Text = "FS",     Width = 70  },
                new ColumnHeader { Text = "Flags",  Width = 130 },
            });
            _partList.SelectedIndexChanged += PartList_SelChanged;

            var leftSplit = new SplitContainer
            {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                SplitterWidth = 5, BackColor = BG,
            };
            leftSplit.SplitterDistance = 200;
            leftSplit.Panel1.Controls.Add(_diskList);
            leftSplit.Panel1.Controls.Add(lblDisks);
            leftSplit.Panel2.Controls.Add(_partList);
            leftSplit.Panel2.Controls.Add(lblParts);
            _diskSplit.Panel1.Controls.Add(leftSplit);

            _diskDetail = MakeRichText();
            _diskDetail.Dock = DockStyle.Fill;
            _diskDetail.Font = FONT_MONO;
            _diskDetail.ReadOnly = true;
            _diskSplit.Panel2.Controls.Add(_diskDetail);

            page.Controls.Add(_diskSplit);
            page.Controls.Add(refreshBtn);
        }

        private void DiskList_SelChanged(object? sender, EventArgs e)
        {
            if (_diskList.SelectedItems.Count == 0) return;
            if (_diskList.SelectedItems[0].Tag is PhysicalDisk disk)
            {
                ShowDiskDetail(disk);
                PopulatePartitions(disk);
            }
        }

        private void PartList_SelChanged(object? sender, EventArgs e)
        {
            if (_partList.SelectedItems.Count == 0) return;
            if (_partList.SelectedItems[0].Tag is PartitionInfo part) ShowPartDetail(part);
        }

        private void PopulatePartitions(PhysicalDisk disk)
        {
            _partList.BeginUpdate();
            _partList.Items.Clear();
            foreach (var p in disk.Partitions)
            {
                var flags = new List<string>();
                if (p.IsBootable) flags.Add("Boot");
                if (p.IsActive)   flags.Add("Active");
                if (p.IsSystem)   flags.Add("System");
                if (p.IsHidden)   flags.Add("Hidden");
                if (p.IsRecovery) flags.Add("Recovery");

                var item = new ListViewItem(p.PartitionNumber.ToString()) { Tag = p };
                item.SubItems.Add(p.DriveLetter ?? "-");
                item.SubItems.Add(p.PartitionType.Length > 28 ? p.PartitionType[..28] + "..." : p.PartitionType);
                item.SubItems.Add(DiskEnumerator.FormatSize(p.PartitionLength));
                item.SubItems.Add($"{p.StartingOffset / (1024 * 1024):N0} MB");
                item.SubItems.Add(p.Volume?.FileSystem ?? "-");
                item.SubItems.Add(string.Join(", ", flags));
                item.ForeColor = p.IsSystem   ? YELLOW : p.IsRecovery ? RED : p.IsHidden ? FG_DIM : FG;
                _partList.Items.Add(item);
            }
            _partList.EndUpdate();
        }

        private void ShowDiskDetail(PhysicalDisk disk)
        {
            _diskDetail.Clear();
            _diskDetail.BackColor = BG2;
            AppendLine(_diskDetail, $"  DISK {disk.DiskIndex}  -  {disk.Model}", FG, FONT_H1, BG3);
            AppendLine(_diskDetail, "", FG, FONT_MAIN);
            AppendPair(_diskDetail, "  Device Path",     disk.DevicePath);
            AppendPair(_diskDetail, "  Model",           disk.Model);
            AppendPair(_diskDetail, "  Serial Number",   disk.SerialNumber.Length > 0 ? disk.SerialNumber : "N/A");
            AppendPair(_diskDetail, "  Media Type",      disk.MediaType);
            AppendPair(_diskDetail, "  Bus / Interface", disk.BusType);
            AppendPair(_diskDetail, "  Firmware Rev",    disk.FirmwareRevision.Length > 0 ? disk.FirmwareRevision : "N/A");
            AppendPair(_diskDetail, "  Total Size",      $"{DiskEnumerator.FormatSize(disk.TotalSize)}  ({disk.TotalSize:N0} bytes)");
            AppendPair(_diskDetail, "  Bytes / Sector",  disk.BytesPerSector.ToString());
            AppendPair(_diskDetail, "  Partition Style", disk.PartitionStyle);
            if (disk.DiskGuid != Guid.Empty)
                AppendPair(_diskDetail, "  Disk GUID", disk.DiskGuid.ToString("B").ToUpper());
            AppendPair(_diskDetail, "  Cylinders",       disk.Cylinders.ToString("N0"));
            AppendPair(_diskDetail, "  Tracks/Cylinder", disk.TracksPerCylinder.ToString());
            AppendPair(_diskDetail, "  Sectors/Track",   disk.SectorsPerTrack.ToString());
            AppendLine(_diskDetail, "", FG, FONT_MAIN);
            AppendLine(_diskDetail, $"  {disk.Partitions.Count} partition(s) detected", ACCENT2, FONT_BOLD);
        }

        private void ShowPartDetail(PartitionInfo part)
        {
            _diskDetail.Clear();
            _diskDetail.BackColor = BG2;
            AppendLine(_diskDetail, $"  PARTITION {part.PartitionNumber}", FG, FONT_H1, BG3);
            AppendLine(_diskDetail, "", FG, FONT_MAIN);
            AppendPair(_diskDetail, "  Drive Letter",  part.DriveLetter ?? "Not mounted");
            AppendPair(_diskDetail, "  Type",          part.PartitionType);
            AppendPair(_diskDetail, "  Size",          $"{DiskEnumerator.FormatSize(part.PartitionLength)}  ({part.PartitionLength:N0} bytes)");
            AppendPair(_diskDetail, "  Start Offset",  $"{part.StartingOffset:N0} bytes  ({part.StartingOffset / (1024 * 1024):N0} MB)");
            AppendPair(_diskDetail, "  End Offset",    $"{part.StartingOffset + part.PartitionLength:N0} bytes");
            AppendLine(_diskDetail, "", FG, FONT_MAIN);

            var flags = new[] {
                ("Bootable",  part.IsBootable, GREEN),
                ("Active",    part.IsActive,   GREEN),
                ("System",    part.IsSystem,   YELLOW),
                ("Hidden",    part.IsHidden,   WARN),
                ("Recovery",  part.IsRecovery, RED),
                ("Read-Only", part.IsReadOnly, FG_DIM),
            };
            _diskDetail.SelectionIndent = 10;
            foreach (var (label, set, col) in flags)
            {
                _diskDetail.SelectionColor = set ? col : GRID_LINE;
                _diskDetail.AppendText($"[{(set ? "o" : ".")} {label}]  ");
            }
            _diskDetail.AppendText("\n\n");

            if (part.Volume != null)
            {
                var v = part.Volume;
                AppendLine(_diskDetail, "  VOLUME DETAILS", FG, FONT_H2, BG3);
                AppendLine(_diskDetail, "", FG, FONT_MAIN);
                AppendPair(_diskDetail, "  Label",        v.VolumeLabel.Length > 0 ? v.VolumeLabel : "(no label)");
                AppendPair(_diskDetail, "  File System",  v.FileSystem);
                AppendPair(_diskDetail, "  Drive Type",   v.DriveType);
                AppendPair(_diskDetail, "  Volume GUID",  v.VolumeGuid.Length > 0 ? v.VolumeGuid : "N/A");
                AppendPair(_diskDetail, "  Total",        DiskEnumerator.FormatSize(v.TotalBytes));
                AppendPair(_diskDetail, "  Free",         $"{DiskEnumerator.FormatSize(v.FreeBytes)}  ({100 - v.UsedPercent:F1}%)");
                AppendPair(_diskDetail, "  Used",         $"{DiskEnumerator.FormatSize(v.UsedBytes)}  ({v.UsedPercent:F1}%)");
                AppendPair(_diskDetail, "  Cluster Size", $"{v.ClusterSize:N0} bytes  ({v.SectorsPerCluster} x {v.BytesPerSector})");
                AppendPair(_diskDetail, "  FS Flags",     $"0x{v.FileSystemFlags:X8}  {DescribeFsFlags(v.FileSystemFlags)}");
                AppendLine(_diskDetail, "", FG, FONT_MAIN);
                DrawUsageBar(_diskDetail, v.UsedPercent);

                if (v.NtfsData != null)
                {
                    var n = v.NtfsData;
                    AppendLine(_diskDetail, "\n  NTFS DETAILS", FG, FONT_H2, BG3);
                    AppendLine(_diskDetail, "", FG, FONT_MAIN);
                    AppendPair(_diskDetail, "  MFT Start LCN",   $"{n.MftStartLcn:N0}  (offset: {n.MftOffset:N0} bytes)");
                    AppendPair(_diskDetail, "  MFT Mirror LCN",  $"{n.Mft2StartLcn:N0}");
                    AppendPair(_diskDetail, "  MFT Valid Data",  DiskEnumerator.FormatSize(n.MftValidDataLength));
                    AppendPair(_diskDetail, "  MFT Zone",        $"LCN {n.MftZoneStart:N0} - {n.MftZoneEnd:N0}");
                    AppendPair(_diskDetail, "  Bytes/MFT Rec",   $"{n.BytesPerMftRecord:N0}");
                    AppendPair(_diskDetail, "  Total Clusters",  $"{n.TotalClusters:N0}");
                    AppendPair(_diskDetail, "  Free Clusters",   $"{n.FreeClusters:N0}  ({(double)n.FreeClusters / n.TotalClusters * 100:F1}%)");
                }
            }
        }

        private string DescribeFsFlags(uint flags)
        {
            var parts = new List<string>();
            if ((flags & 0x0001) != 0) parts.Add("CaseSensitive");
            if ((flags & 0x0002) != 0) parts.Add("CasePreserved");
            if ((flags & 0x0004) != 0) parts.Add("UnicodeOnDisk");
            if ((flags & 0x0008) != 0) parts.Add("ACLs");
            if ((flags & 0x0010) != 0) parts.Add("FileCompression");
            if ((flags & 0x0020) != 0) parts.Add("NamedStreams");
            if ((flags & 0x0040) != 0) parts.Add("ReadOnly");
            if ((flags & 0x8000) != 0) parts.Add("Encryption");
            if ((flags & 0x00020000) != 0) parts.Add("WofSupported");
            return string.Join(", ", parts);
        }

        private void DrawUsageBar(RichTextBox rtb, double usedPct)
        {
            int totalChars = 50;
            int usedChars  = (int)(usedPct / 100.0 * totalChars);
            rtb.SelectionIndent = 10;
            rtb.SelectionColor  = FG_DIM;
            rtb.AppendText("  [");
            rtb.SelectionColor = usedPct > 90 ? RED : usedPct > 70 ? WARN : GREEN;
            rtb.AppendText(new string('#', usedChars));
            rtb.SelectionColor = BG3;
            rtb.AppendText(new string('-', totalChars - usedChars));
            rtb.SelectionColor = FG_DIM;
            rtb.AppendText($"]  {usedPct:F1}% used\n");
        }

        // ================================================================
        //  NTFS TAB
        // ================================================================
        private void BuildNtfsTab()
        {
            var page = MakePage("NTFS / MFT");

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 52, BackColor = BG2,
                Padding = new Padding(10, 10, 10, 6),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false, AutoSize = false,
            };

            var lblVol = MakeFlowLabel("Volume:");
            _ntfsVolCombo = new ComboBox
            {
                Width = 130, Height = 28, BackColor = BG3, ForeColor = FG,
                FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(4, 2, 8, 0),
            };

            _btnReadMft = MakeFlowButton("Read 512 MFT Records", ACCENT, 210);
            _btnReadMft.Click += BtnReadMft_Click;

            _btnMftAll = MakeFlowButton("Read ALL (slow)", WARN, 140);
            _btnMftAll.Click += BtnMftAll_Click;

            _mftStatus = new Label
            {
                AutoSize = true, ForeColor = FG_DIM, BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(16, 6, 0, 0), Font = FONT_MAIN,
                Text = "Select a volume and click Read.",
            };

            _mftProgress = new ProgressBar
            {
                Width = 300, Height = 10, BackColor = BG3,
                Visible = false, Margin = new Padding(4, 10, 0, 0),
            };

            flow.Controls.AddRange(new Control[] { lblVol, _ntfsVolCombo, _btnReadMft, _btnMftAll, _mftStatus });

            _mftSplit = new SplitContainer { Dock = DockStyle.Fill, BackColor = BG, SplitterWidth = 5 };
            _mftSplit.SplitterDistance = 720;

            _mftList = MakeListView();
            _mftList.Dock = DockStyle.Fill;
            _mftList.SelectedIndexChanged += MftList_SelChanged;
            _mftList.Columns.AddRange(new[]
            {
                new ColumnHeader { Text = "#",          Width = 75  },
                new ColumnHeader { Text = "Name",       Width = 260 },
                new ColumnHeader { Text = "Status",     Width = 85  },
                new ColumnHeader { Text = "Type",       Width = 65  },
                new ColumnHeader { Text = "Size",       Width = 100, TextAlign = HorizontalAlignment.Right },
                new ColumnHeader { Text = "Modified",   Width = 155 },
                new ColumnHeader { Text = "Hard Links", Width = 80  },
                new ColumnHeader { Text = "Parent #",   Width = 80  },
                new ColumnHeader { Text = "Seq #",      Width = 60  },
            });
            _mftSplit.Panel1.Controls.Add(_mftList);

            _mftDetail = MakeRichText();
            _mftDetail.Dock = DockStyle.Fill;
            _mftDetail.Font = FONT_MONO;
            _mftDetail.ReadOnly = true;
            _mftSplit.Panel2.Controls.Add(_mftDetail);

            page.Controls.Add(_mftSplit);
            page.Controls.Add(flow);
        }

        private void MftList_SelChanged(object? sender, EventArgs e)
        {
            if (_mftList.SelectedItems.Count == 0) return;
            if (_mftList.SelectedItems[0].Tag is MftRecord rec) ShowMftDetail(rec);
        }

        private void ShowMftDetail(MftRecord rec)
        {
            _mftDetail.Clear();
            _mftDetail.BackColor = BG2;
            AppendLine(_mftDetail, $"  MFT RECORD #{rec.RecordNumber}", FG, FONT_H2, BG3);
            AppendLine(_mftDetail, "", FG, FONT_MAIN);
            AppendPair(_mftDetail, "  Status",      rec.StatusDescription);
            AppendPair(_mftDetail, "  In Use",      rec.IsInUse ? "Yes" : "No (deleted)");
            AppendPair(_mftDetail, "  Directory",   rec.IsDirectory ? "Yes" : "No");
            AppendPair(_mftDetail, "  File Name",   rec.FileName.Length > 0 ? rec.FileName : "(no $FILE_NAME attr)");
            AppendPair(_mftDetail, "  Parent Rec#", rec.ParentRecordNumber.ToString());
            AppendPair(_mftDetail, "  File Size",   rec.FileSize > 0 ? DiskEnumerator.FormatSize(rec.FileSize) : "-");
            AppendPair(_mftDetail, "  Alloc Size",  rec.AllocatedSize > 0 ? DiskEnumerator.FormatSize(rec.AllocatedSize) : "-");
            AppendPair(_mftDetail, "  Hard Links",  rec.HardLinkCount.ToString());
            AppendPair(_mftDetail, "  Sequence #",  rec.SequenceNumber.ToString());
            AppendPair(_mftDetail, "  Flags",       $"0x{rec.Flags:X4}");
            AppendLine(_mftDetail, "", FG, FONT_MAIN);
            if (rec.CreationTime != DateTime.MinValue)
            {
                AppendPair(_mftDetail, "  Created",  rec.CreationTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                AppendPair(_mftDetail, "  Modified", rec.LastModifiedTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                AppendPair(_mftDetail, "  MFT Mod",  rec.MftModifiedTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                AppendPair(_mftDetail, "  Accessed", rec.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            }
            if (rec.Attributes.Count > 0)
            {
                AppendLine(_mftDetail, "\n  ATTRIBUTES", FG, FONT_H2, BG3);
                foreach (var attr in rec.Attributes)
                {
                    AppendLine(_mftDetail, "", FG, FONT_MAIN);
                    AppendLine(_mftDetail, $"  [{attr.TypeCode:X8}] {attr.TypeName}" +
                               (attr.AttributeName != null ? $" ({attr.AttributeName})" : "") +
                               (attr.IsNonResident ? "  [non-resident]" : "  [resident]"),
                               ACCENT2, FONT_BOLD);
                    AppendPair(_mftDetail, "    Length", $"{attr.Length:N0} bytes");
                    if (!string.IsNullOrEmpty(attr.ParsedDescription))
                        AppendPair(_mftDetail, "    Value", attr.ParsedDescription);
                }
            }
            if (rec.RawBytes != null)
            {
                AppendLine(_mftDetail, "\n  RAW RECORD (1024 bytes)", FG, FONT_H2, BG3);
                AppendLine(_mftDetail, "", FG, FONT_MAIN);
                _mftDetail.SelectionFont  = FONT_MONO;
                _mftDetail.SelectionColor = ACCENT2;
                _mftDetail.AppendText(NtfsReader.HexDump(rec.RawBytes, 256));
            }
        }

        private async void BtnReadMft_Click(object? sender, EventArgs e) => await ReadMftAsync(512);
        private async void BtnMftAll_Click(object? sender, EventArgs e)  => await ReadMftAsync(0);

        private async Task ReadMftAsync(int maxRecords)
        {
            if (_ntfsVolCombo.SelectedItem == null) return;
            string vol = _ntfsVolCombo.SelectedItem.ToString()!;
            var volData = NtfsReader.GetVolumeData(vol + "\\");
            if (volData == null)
            {
                MessageBox.Show("Could not read NTFS volume data.\nRun as Administrator for raw MFT access.",
                    "Access Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _btnReadMft.Enabled = false;
            _btnMftAll.Enabled  = false;
            _mftStatus.Text     = "Reading MFT...";
            _mftProgress.Visible = true;
            _mftProgress.Value   = 0;

            long total = volData.MftValidDataLength / 1024;
            _mftProgress.Maximum = (int)Math.Min(total, maxRecords > 0 ? maxRecords : total);

            var progress = new Progress<(int done, int total)>(p =>
            {
                _mftProgress.Value = Math.Min(p.done, _mftProgress.Maximum);
                _mftStatus.Text = $"Reading... {p.done:N0} / {p.total:N0} records";
            });

            List<MftRecord> records;
            try
            {
                records = await Task.Run(() => NtfsReader.ReadMftRecords(vol, volData, maxRecords, progress));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"MFT read error:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                records = new();
            }

            _mftList.BeginUpdate();
            _mftList.Items.Clear();
            foreach (var rec in records)
            {
                var item = new ListViewItem(rec.RecordNumber.ToString()) { Tag = rec };
                item.SubItems.Add(rec.FileName);
                item.SubItems.Add(rec.StatusDescription);
                item.SubItems.Add(rec.IsDirectory ? "Dir" : "File");
                item.SubItems.Add(rec.FileSize > 0 ? DiskEnumerator.FormatSize(rec.FileSize) : "");
                item.SubItems.Add(rec.LastModifiedTime != DateTime.MinValue ? rec.LastModifiedTime.ToString("yyyy-MM-dd HH:mm:ss") : "");
                item.SubItems.Add(rec.HardLinkCount.ToString());
                item.SubItems.Add(rec.ParentRecordNumber.ToString());
                item.SubItems.Add(rec.SequenceNumber.ToString());
                item.ForeColor = !rec.IsInUse       ? RED
                               : rec.RecordNumber < 12 ? YELLOW
                               : rec.IsDirectory   ? ACCENT
                               : FG;
                _mftList.Items.Add(item);
            }
            _mftList.EndUpdate();
            _mftStatus.Text = $"Loaded {records.Count:N0} records  ({records.Count(r => !r.IsInUse):N0} deleted)";
            _mftProgress.Visible = false;
            _btnReadMft.Enabled = true;
            _btnMftAll.Enabled  = true;
        }

        // ================================================================
        //  HEX VIEWER TAB
        // ================================================================
        private void BuildHexTab()
        {
            var page = MakePage("Hex Viewer");

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 52, BackColor = BG2,
                Padding = new Padding(10, 10, 10, 6),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false, AutoSize = false,
            };

            var lblTarget = MakeFlowLabel("Target:");
            _hexTargetCombo = new ComboBox
            {
                Width = 180, Height = 28, BackColor = BG3, ForeColor = FG,
                FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(4, 2, 8, 0),
            };
            _hexTargetCombo.Items.AddRange(new object[] { "File", "Physical Drive", "Volume (raw)" });
            _hexTargetCombo.SelectedIndex = 0;

            _hexPathBox = new TextBox
            {
                Width = 480, Height = 28, BackColor = BG3, ForeColor = FG,
                BorderStyle = BorderStyle.FixedSingle, Font = FONT_MONO,
                PlaceholderText = "C:\\file  or  \\\\.\\PhysicalDrive0  or  \\\\.\\C:",
                Margin = new Padding(0, 2, 4, 0),
            };
            _btnHexBrowse = MakeFlowButton("...", BG3, 38);
            _btnHexBrowse.Click += HexBrowse_Click;

            var lblOff = MakeFlowLabel("Offset:");
            _hexOffset = new NumericUpDown
            {
                Width = 130, Height = 28, BackColor = BG3, ForeColor = FG,
                BorderStyle = BorderStyle.FixedSingle,
                Minimum = 0, Maximum = long.MaxValue, Increment = 512, DecimalPlaces = 0,
                Margin = new Padding(4, 2, 8, 0),
            };

            var lblLen = MakeFlowLabel("Length:");
            _hexLength = new NumericUpDown
            {
                Width = 90, Height = 28, BackColor = BG3, ForeColor = FG,
                BorderStyle = BorderStyle.FixedSingle,
                Minimum = 16, Maximum = 65536, Value = 512, Increment = 512,
                Margin = new Padding(4, 2, 8, 0),
            };

            _btnHexRead = MakeFlowButton("Read", ACCENT, 72);
            _btnHexRead.Click += HexRead_Click;

            flow.Controls.AddRange(new Control[] {
                lblTarget, _hexTargetCombo, _hexPathBox, _btnHexBrowse,
                lblOff, _hexOffset, lblLen, _hexLength, _btnHexRead
            });

            _hexView = MakeRichText();
            _hexView.Dock = DockStyle.Fill;
            _hexView.Font = FONT_MONO;
            _hexView.ReadOnly = true;
            _hexView.WordWrap = false;

            page.Controls.Add(_hexView);
            page.Controls.Add(flow);
        }

        private void HexBrowse_Click(object? sender, EventArgs e)
        {
            if (_hexTargetCombo.SelectedIndex == 0)
            {
                using var ofd = new OpenFileDialog { Title = "Open file for hex viewing", Filter = "All Files|*.*" };
                if (ofd.ShowDialog() == DialogResult.OK) _hexPathBox.Text = ofd.FileName;
            }
        }

        private async void HexRead_Click(object? sender, EventArgs e)
        {
            string path = _hexPathBox.Text.Trim();
            if (path.Length == 0) return;
            long offset = (long)_hexOffset.Value;
            int length  = (int)_hexLength.Value;
            _btnHexRead.Enabled = false;
            try
            {
                byte[] data = await Task.Run(() =>
                {
                    IntPtr h = NativeMethods.CreateFile(path,
                        NativeMethods.GENERIC_READ,
                        NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                        IntPtr.Zero, NativeMethods.OPEN_EXISTING,
                        NativeMethods.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
                    if (h == NativeMethods.INVALID_HANDLE_VALUE)
                        throw new System.ComponentModel.Win32Exception();
                    try
                    {
                        NativeMethods.SetFilePointerEx(h, offset, out _, 0);
                        byte[] buf = new byte[length];
                        NativeMethods.ReadFile(h, buf, (uint)length, out uint read, IntPtr.Zero);
                        if (read < length) { var t = new byte[read]; Array.Copy(buf, t, (int)read); return t; }
                        return buf;
                    }
                    finally { NativeMethods.CloseHandle(h); }
                });
                _hexView.Clear();
                _hexView.BackColor = BG;
                _hexView.SelectionFont  = FONT_MONO;
                _hexView.SelectionColor = FG_DIM;
                _hexView.AppendText($"// {path}  |  Offset: 0x{offset:X8} ({offset:N0})  |  {data.Length} bytes\n\n");
                _hexView.SelectionColor = ACCENT2;
                _hexView.AppendText(NtfsReader.HexDump(data, data.Length));
                _statusLabel.Text = $"Hex: {path}  offset={offset:N0}  length={data.Length}";
            }
            catch (Exception ex)
            {
                _hexView.Clear();
                _hexView.SelectionColor = RED;
                _hexView.AppendText($"Error reading:\n{ex.Message}\n\nHint: Run as Administrator for raw device access.");
            }
            finally { _btnHexRead.Enabled = true; }
        }

        // ================================================================
        //  LOAD / INIT
        // ================================================================
        private async void OnLoad(object? sender, EventArgs e)
        {
            bool elevated = DiskEnumerator.IsElevated();
            _elevLabel.Text     = elevated ? "  [Admin]  " : "  [Not Admin]  ";
            _elevLabel.ForeColor = elevated ? GREEN : WARN;

            PopulateDriveTree();

            foreach (string d in Directory.GetLogicalDrives())
            {
                var vi = DiskEnumerator.GetVolumeInfo(d);
                if (vi?.FileSystem == "NTFS") _ntfsVolCombo.Items.Add(d.TrimEnd('\\'));
            }
            if (_ntfsVolCombo.Items.Count > 0) _ntfsVolCombo.SelectedIndex = 0;

            foreach (string d in Directory.GetLogicalDrives())
                _hexTargetCombo.Items.Add($"Volume: {d.TrimEnd('\\')}");
            for (int i = 0; i < 4; i++)
                _hexTargetCombo.Items.Add($"PhysicalDrive{i}");

            _statusLabel.Text = "Loading disk information...";
            await Task.Run(() => { _disks = DiskEnumerator.EnumeratePhysicalDisks(); });
            LoadDisks();

            StartIndex();
        }

        private void StartIndex()
        {
            _statusLabel.Text = "Building file index...";
            FileSystemBrowser.IndexProgressChanged += count =>
                Invoke(() => _indexLabel.Text = $"Indexing... {count:N0} files");
            FileSystemBrowser.IndexStatusChanged += msg =>
                Invoke(() => _indexLabel.Text = msg);
            FileSystemBrowser.IndexCompleted += () =>
                Invoke(() =>
                {
                    _statusLabel.Text = "Ready";
                    _indexLabel.Text  = $"Index: {FileSystemBrowser.IndexedCount:N0} items";
                    DoSearch();
                });
            FileSystemBrowser.StartIndexing(true, true);
        }

        private void LoadDisks()
        {
            _diskList.BeginUpdate();
            _diskList.Items.Clear();
            foreach (var disk in _disks)
            {
                var item = new ListViewItem(disk.DiskIndex.ToString()) { Tag = disk };
                item.SubItems.Add(disk.Model);
                item.SubItems.Add(DiskEnumerator.FormatSize(disk.TotalSize));
                item.SubItems.Add(disk.PartitionStyle);
                _diskList.Items.Add(item);
            }
            _diskList.EndUpdate();
            if (_diskList.Items.Count > 0) { _diskList.Items[0].Selected = true; _diskList.Select(); }
        }

        private void PopulateDriveTree()
        {
            _fsTree.BeginUpdate();
            _fsTree.Nodes.Clear();
            foreach (string drive in Directory.GetLogicalDrives())
            {
                var vi    = DiskEnumerator.GetVolumeInfo(drive);
                string label = vi?.VolumeLabel.Length > 0 ? $"{vi.VolumeLabel} ({drive.TrimEnd('\\')})" : drive.TrimEnd('\\');
                string fs    = vi?.FileSystem ?? "";
                string text  = fs.Length > 0 ? $"{label}  [{fs}]" : label;
                var node = new TreeNode(text) { Tag = drive, ForeColor = ACCENT };
                node.NodeFont = FONT_BOLD;
                try
                {
                    if (Directory.EnumerateDirectories(drive, "*",
                        new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true }).Any())
                        node.Nodes.Add(new TreeNode("...") { Tag = "DUMMY" });
                }
                catch { }
                _fsTree.Nodes.Add(node);
            }
            _fsTree.EndUpdate();
        }

        private void LoadDirectory(string path)
        {
            _pathBox.Text     = path;
            _statusLabel.Text = $"Loading {path}...";
            _fsList.BeginUpdate();
            _fsList.Items.Clear();

            var entries = FileSystemBrowser.GetEntries(path, true, true);

            if (!FileSystemBrowser.IsRootPath(path))
            {
                string? parent = Path.GetDirectoryName(path);
                if (parent != null)
                {
                    var up = new ListViewItem("..") { Tag = new FileSystemEntry { FullPath = parent, IsDirectory = true }, ForeColor = FG_DIM };
                    up.SubItems.AddRange(new[] { "", "", "", "D", "Parent", "" });
                    _fsList.Items.Add(up);
                }
            }
            foreach (var entry in entries)
            {
                var item = new ListViewItem(entry.Name) { Tag = entry };
                item.ForeColor = entry.IsDirectory ? ACCENT
                               : entry.IsSystem    ? WARN
                               : entry.IsHidden    ? FG_DIM
                               : FG;
                item.SubItems.Add(entry.IsDirectory ? "" : (entry.Size >= 0 ? DiskEnumerator.FormatSize((ulong)entry.Size) : "N/A"));
                item.SubItems.Add(FmtTime(entry.LastWriteTime));
                item.SubItems.Add(FmtTime(entry.CreationTime));
                item.SubItems.Add(entry.AttributeString);
                item.SubItems.Add(entry.IsDirectory ? "Folder" : (entry.Extension.Length > 0 ? entry.Extension.ToUpperInvariant() : "File"));
                item.SubItems.Add(entry.LinkTarget ?? "");
                _fsList.Items.Add(item);
            }
            _fsList.EndUpdate();
            _statusLabel.Text = $"{entries.Count:N0} items in {path}";
        }

        private void NavigateTo(string path)
        {
            if (File.Exists(path)) { string? dir = Path.GetDirectoryName(path); if (dir != null) LoadDirectory(dir); return; }
            if (Directory.Exists(path)) LoadDirectory(path);
        }

        private void NavigateUp()
        {
            string current = _pathBox.Text;
            if (FileSystemBrowser.IsRootPath(current)) return;
            string? parent = Path.GetDirectoryName(current);
            if (parent != null) LoadDirectory(parent);
        }

        private void ExpandTreeTo(string targetPath)
        {
            foreach (TreeNode node in _fsTree.Nodes)
            {
                if (targetPath.StartsWith(node.Tag?.ToString() ?? "", StringComparison.OrdinalIgnoreCase))
                { node.Expand(); break; }
            }
        }

        // ================================================================
        //  UI HELPERS
        // ================================================================
        private TabPage MakePage(string title)
        {
            var page = new TabPage(title) { BackColor = BG, ForeColor = FG, Padding = new Padding(0) };
            _tabs.TabPages.Add(page);
            return page;
        }

        private ListView MakeListView()
        {
            var lv = new ListView
            {
                View = View.Details, FullRowSelect = true, GridLines = false,
                BackColor = BG, ForeColor = FG, BorderStyle = BorderStyle.None,
                Font = FONT_MAIN, HeaderStyle = ColumnHeaderStyle.Clickable,
                HideSelection = false, MultiSelect = false,
                SmallImageList = ROW_HEIGHT,   // forces 24px row height
            };
            lv.ColumnClick += (s, e) => SortColumn((ListView)s!, e.Column);
            return lv;
        }

        private static void SortColumn(ListView lv, int col)
        {
            var items = new List<ListViewItem>(lv.Items.Cast<ListViewItem>());
            bool desc = lv.Tag is int c && c == col;
            items.Sort((a, b) => desc
                ? string.Compare(b.SubItems[col].Text, a.SubItems[col].Text, StringComparison.OrdinalIgnoreCase)
                : string.Compare(a.SubItems[col].Text, b.SubItems[col].Text, StringComparison.OrdinalIgnoreCase));
            lv.BeginUpdate();
            lv.Items.Clear();
            lv.Items.AddRange(items.ToArray());
            lv.EndUpdate();
            lv.Tag = desc ? -1 : col;
        }

        private RichTextBox MakeRichText() =>
            new() { BackColor = BG2, ForeColor = FG, BorderStyle = BorderStyle.None, Font = FONT_MAIN };

        // Flow-layout helper controls (auto-size, correct margins)
        private Label MakeFlowLabel(string text) => new()
        {
            Text = text, AutoSize = false, Width = 72, Height = 28,
            ForeColor = FG_DIM, BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 4, 4, 0), Font = FONT_MAIN,
        };

        private CheckBox MakeFlowCheck(string text) => new()
        {
            Text = text, AutoSize = true,
            ForeColor = FG, BackColor = Color.Transparent,
            Margin = new Padding(8, 6, 4, 0), Font = FONT_MAIN,
        };

        private Button MakeFlowButton(string text, Color bg, int width) => new()
        {
            Text = text, Width = width, Height = 28,
            BackColor = bg, ForeColor = FG, FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            Font = FONT_MAIN, Cursor = Cursors.Hand,
            Margin = new Padding(6, 2, 0, 0),
        };

        private void AppendLine(RichTextBox rtb, string text, Color color, Font font, Color? bg = null)
        {
            if (bg.HasValue) { rtb.SelectionBackColor = bg.Value; rtb.SelectionColor = color; rtb.SelectionFont = font; rtb.AppendText(text + "\n"); rtb.SelectionBackColor = BG2; }
            else             { rtb.SelectionColor = color; rtb.SelectionFont = font; rtb.AppendText(text + "\n"); }
        }

        private void AppendPair(RichTextBox rtb, string label, string value)
        {
            rtb.SelectionColor = FG_DIM; rtb.SelectionFont = FONT_MAIN;
            rtb.AppendText(label.PadRight(22) + "  ");
            rtb.SelectionColor = FG;
            rtb.AppendText(value + "\n");
        }

        private string FmtTime(DateTime dt) =>
            dt == DateTime.MinValue ? "N/A" : dt.ToString("yyyy-MM-dd  HH:mm:ss");
    }
}
