using Microsoft.Extensions.Logging;
using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;
using SmbEnterprise.WinFormsApp.Services;
using SmbEnterprise.WinFormsApp.Themes;
using SmbEnterprise.WinFormsApp.Transfer;

namespace SmbEnterprise.WinFormsApp;

public sealed class MainForm : Form
{
    private enum ClipboardMode
    {
        None,
        Copy,
        Cut
    }

    private readonly TransferViewModel _viewModel;
    private readonly TransferQueueController _transferQueue;
    private readonly AppThemeManager _themeManager;
    private readonly UiLogStore _logStore;
    private readonly ILogger<MainForm> _logger;
    private readonly SettingsManager _settingsManager;
    private readonly IServiceProvider _sp;

    private readonly Dictionary<Guid, ListViewItem> _transferRows = new();
    private readonly Dictionary<Guid, string> _pendingDeleteAfterTransfer = new();
    private readonly List<FileItem> _currentFolderItems = [];
    private readonly List<(string RemotePath, bool IsDirectory)> _clipboardItems = [];

    private IRemoteFileSystem? _fileSystem;
    private SmbSettings _settings;
    private ClipboardMode _clipboardMode = ClipboardMode.None;
    private string _selectedDestinationPath = string.Empty;

    private TextBox tbServer = null!;
    private TextBox tbShare = null!;
    private TextBox tbUsername = null!;
    private TextBox tbPassword = null!;
    private TextBox tbAddress = null!;
    private TextBox tbDestination = null!;

    private Button btnConnect = null!;
    private Button btnDisconnect = null!;
    private Button btnRefresh = null!;
    private Button btnTransfer = null!;
    private Button btnCopy = null!;
    private Button btnCut = null!;
    private Button btnPaste = null!;
    private Button btnDelete = null!;
    private Button btnRename = null!;
    private Button btnNewFolder = null!;
    private Button btnCancelTransfer = null!;
    private Button btnPauseTransfer = null!;
    private Button btnResumeTransfer = null!;
    private Button btnRetryTransfer = null!;
    private Button btnTheme = null!;
    private Button btnFullTest = null!;

    private Label lblStatus = null!;
    private Label lblInfo = null!;

    private TreeView tvNavigation = null!;
    private TreeView tvDestination = null!;
    private ListView lvFiles = null!;
    private ListView lvTransfers = null!;
    private TextBox tbLogs = null!;

    public MainForm(
        TransferViewModel viewModel,
        TransferQueueController transferQueue,
        AppThemeManager themeManager,
        UiLogStore logStore,
        ILogger<MainForm> logger,
        IServiceProvider sp)
    {
        _viewModel = viewModel;
        _transferQueue = transferQueue;
        _themeManager = themeManager;
        _logStore = logStore;
        _logger = logger;
        _sp = sp;
        _settingsManager = new SettingsManager();
        _settings = _settingsManager.Load();

        InitializeComponent();
        WireEvents();
        ApplySettingsToInputs();
        LoadExistingLogs();
        InitializeDestinationTree();
        _themeManager.ApplyTheme(this, AppTheme.Light);
    }

    private void InitializeComponent()
    {
        Text = "SmbEnterprise Explorer";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1700;
        Height = 980;

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));

        main.Controls.Add(BuildRibbonPanel(), 0, 0);
        main.Controls.Add(BuildPathPanel(), 0, 1);
        main.Controls.Add(BuildExplorerArea(), 0, 2);
        main.Controls.Add(BuildBottomPanel(), 0, 3);

        Controls.Add(main);
    }

    private Control BuildRibbonPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(8)
        };

        tbServer = BuildTextInput("Server", 150);
        tbShare = BuildTextInput("Share", 140);
        tbUsername = BuildTextInput("User", 120);
        tbPassword = BuildTextInput("Password", 120, true);

        btnConnect = BuildButton("Connect", BtnConnect_Click);
        btnDisconnect = BuildButton("Disconnect", BtnDisconnect_Click);
        btnRefresh = BuildButton("Refresh", BtnRefresh_Click);

        btnCopy = BuildButton("Copy", BtnCopy_Click);
        btnCut = BuildButton("Cut", BtnCut_Click);
        btnPaste = BuildButton("Paste", BtnPaste_Click);
        btnDelete = BuildButton("Delete", BtnDelete_Click);
        btnRename = BuildButton("Rename", BtnRename_Click);
        btnNewFolder = BuildButton("New Folder", BtnNewFolder_Click);

        btnTransfer = BuildButton("Queue Transfer", BtnTransfer_Click);
        btnCancelTransfer = BuildButton("Cancel", BtnCancelTransfer_Click);
        btnPauseTransfer = BuildButton("Pause", BtnPauseTransfer_Click);
        btnResumeTransfer = BuildButton("Resume", BtnResumeTransfer_Click);
        btnRetryTransfer = BuildButton("Retry", BtnRetryTransfer_Click);
        btnTheme = BuildButton("Theme", BtnTheme_Click);
        btnFullTest = BuildButton("Full Test", BtnFullTest_Click);
        btnFullTest.BackColor = Color.FromArgb(0, 122, 204);
        btnFullTest.ForeColor = Color.White;

        btnDisconnect.Enabled = false;

        lblStatus = new Label
        {
            AutoSize = true,
            Text = "Offline",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 10, 0, 0)
        };

        panel.Controls.Add(tbServer);
        panel.Controls.Add(tbShare);
        panel.Controls.Add(tbUsername);
        panel.Controls.Add(tbPassword);
        panel.Controls.Add(btnConnect);
        panel.Controls.Add(btnDisconnect);
        panel.Controls.Add(btnRefresh);
        panel.Controls.Add(btnCopy);
        panel.Controls.Add(btnCut);
        panel.Controls.Add(btnPaste);
        panel.Controls.Add(btnDelete);
        panel.Controls.Add(btnRename);
        panel.Controls.Add(btnNewFolder);
        panel.Controls.Add(btnTransfer);
        panel.Controls.Add(btnPauseTransfer);
        panel.Controls.Add(btnResumeTransfer);
        panel.Controls.Add(btnRetryTransfer);
        panel.Controls.Add(btnCancelTransfer);
        panel.Controls.Add(btnTheme);
        panel.Controls.Add(btnFullTest);
        panel.Controls.Add(lblStatus);

        return panel;
    }

    private Control BuildPathPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 5, 8, 5),
            ColumnCount = 6,
            RowCount = 1
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));

        panel.Controls.Add(new Label { Text = "SMB:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);

        tbAddress = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "\\server\\share\\folder"
        };
        panel.Controls.Add(tbAddress, 1, 0);

        var btnGo = new Button { Dock = DockStyle.Fill, Text = "Go SMB" };
        btnGo.Click += async (_, _) => await NavigateToPathAsync(tbAddress.Text, CancellationToken.None);
        panel.Controls.Add(btnGo, 2, 0);

        panel.Controls.Add(new Label { Text = "Dest:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 3, 0);

        tbDestination = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "C:\\Output"
        };
        panel.Controls.Add(tbDestination, 4, 0);

        var btnBrowse = new Button { Dock = DockStyle.Fill, Text = "Browse" };
        btnBrowse.Click += BtnBrowseDestination_Click;
        panel.Controls.Add(btnBrowse, 5, 0);

        return panel;
    }

    private Control BuildExplorerArea()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 460
        };

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8)
        };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        left.Controls.Add(new Label { Text = "SMB Tree", Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        tvNavigation = new TreeView { Dock = DockStyle.Fill, HideSelection = false };
        left.Controls.Add(tvNavigation, 0, 1);

        left.Controls.Add(new Label { Text = "Destination Tree (Local)", Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold) }, 0, 2);
        tvDestination = new TreeView { Dock = DockStyle.Fill, HideSelection = false };
        left.Controls.Add(tvDestination, 0, 3);

        split.Panel1.Controls.Add(left);

        lvFiles = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            VirtualMode = true,
            MultiSelect = true
        };
        lvFiles.Columns.Add("Name", 340);
        lvFiles.Columns.Add("Size", 120);
        lvFiles.Columns.Add("Modified", 180);
        lvFiles.Columns.Add("Attributes", 140);
        lvFiles.Columns.Add("Transfer Status", 130);

        split.Panel2.Padding = new Padding(8);
        split.Panel2.Controls.Add(lvFiles);

        return split;
    }

    private Control BuildBottomPanel()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };

        var tabTransfers = new TabPage("Transfer Manager");
        lvTransfers = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true
        };
        lvTransfers.Columns.Add("File", 250);
        lvTransfers.Columns.Add("Progress", 90);
        lvTransfers.Columns.Add("Speed", 95);
        lvTransfers.Columns.Add("ETA", 90);
        lvTransfers.Columns.Add("Retries", 60);
        lvTransfers.Columns.Add("Stage", 90);
        lvTransfers.Columns.Add("Source", 300);
        lvTransfers.Columns.Add("Destination", 300);
        tabTransfers.Controls.Add(lvTransfers);

        var tabLogs = new TabPage("Logs");
        tbLogs = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true
        };
        tabLogs.Controls.Add(tbLogs);

        tabs.TabPages.Add(tabTransfers);
        tabs.TabPages.Add(tabLogs);

        lblInfo = new Label
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Text = "Ready",
            Padding = new Padding(8, 4, 0, 4)
        };

        var holder = new Panel { Dock = DockStyle.Fill };
        holder.Controls.Add(tabs);
        holder.Controls.Add(lblInfo);
        return holder;
    }

    private static TextBox BuildTextInput(string placeholder, int width, bool isPassword = false)
    {
        return new TextBox
        {
            Width = width,
            PlaceholderText = placeholder,
            UseSystemPasswordChar = isPassword,
            Margin = new Padding(0, 8, 8, 8)
        };
    }

    private static Button BuildButton(string text, EventHandler click)
    {
        var button = new Button
        {
            Width = 98,
            Height = 30,
            Text = text,
            Margin = new Padding(0, 8, 8, 8)
        };
        button.Click += click;
        return button;
    }

    private void WireEvents()
    {
        FormClosing += MainForm_FormClosing;

        tvNavigation.BeforeExpand += TvNavigation_BeforeExpand;
        tvNavigation.AfterSelect += TvNavigation_AfterSelect;

        tvDestination.BeforeExpand += TvDestination_BeforeExpand;
        tvDestination.AfterSelect += TvDestination_AfterSelect;

        lvFiles.RetrieveVirtualItem += LvFiles_RetrieveVirtualItem;
        lvFiles.DoubleClick += LvFiles_DoubleClick;
        lvFiles.ContextMenuStrip = BuildRemoteContextMenu();

        tvDestination.ContextMenuStrip = BuildDestinationContextMenu();

        _transferQueue.ItemUpdated += TransferQueue_ItemUpdated;
        _logStore.LogAdded += LogStore_LogAdded;
    }

    private ContextMenuStrip BuildRemoteContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Copy", null, BtnCopy_Click);
        menu.Items.Add("Cut", null, BtnCut_Click);
        menu.Items.Add("Paste to destination", null, BtnPaste_Click);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Delete", null, BtnDelete_Click);
        menu.Items.Add("Rename", null, BtnRename_Click);
        menu.Items.Add("New Folder", null, BtnNewFolder_Click);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Queue Transfer", null, BtnTransfer_Click);
        return menu;
    }

    private ContextMenuStrip BuildDestinationContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Set as destination", null, (_, _) => SetDestinationFromSelectedNode());
        menu.Items.Add("Refresh", null, (_, _) => RefreshDestinationNode());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("New Folder", null, (_, _) => CreateLocalFolder());
        menu.Items.Add("Rename", null, (_, _) => RenameLocalNode());
        menu.Items.Add("Delete", null, (_, _) => DeleteLocalNode());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Paste", null, BtnPaste_Click);
        return menu;
    }

    private void ApplySettingsToInputs()
    {
        tbServer.Text = _settings.Server;
        tbShare.Text = _settings.Share;
        tbUsername.Text = _settings.Username;
    }

    private void LoadExistingLogs()
    {
        var lines = _logStore.Snapshot();
        if (lines.Count == 0)
        {
            return;
        }

        tbLogs.Lines = lines.ToArray();
        tbLogs.SelectionStart = tbLogs.Text.Length;
        tbLogs.ScrollToCaret();
    }

    private void InitializeDestinationTree()
    {
        tvDestination.BeginUpdate();
        tvDestination.Nodes.Clear();

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var node = new TreeNode($"{drive.Name} ({drive.DriveFormat})")
            {
                Tag = drive.RootDirectory.FullName
            };
            node.Nodes.Add(new TreeNode());
            tvDestination.Nodes.Add(node);
        }

        tvDestination.EndUpdate();
    }

    private async void BtnConnect_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(tbServer.Text) || string.IsNullOrWhiteSpace(tbShare.Text))
        {
            MessageBox.Show("Server and Share are required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        btnConnect.Enabled = false;

        try
        {
            var credential = new RemoteCredential
            {
                Server = tbServer.Text.Trim(),
                Share = tbShare.Text.Trim(),
                Username = tbUsername.Text.Trim(),
                Password = tbPassword.Text,
                Port = 445
            };

            _fileSystem = await _viewModel.ConnectAsync(credential);

            lblStatus.Text = $"Online: {credential.Server}\\{credential.Share}";
            btnDisconnect.Enabled = true;
            await LoadRootTreeAsync(CancellationToken.None);

            _settings.Server = credential.Server;
            _settings.Share = credential.Share;
            _settings.Username = credential.Username;
            _settingsManager.Save(_settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connect failed");
            MessageBox.Show($"Connection failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            btnConnect.Enabled = true;
        }
    }

    private async void BtnDisconnect_Click(object? sender, EventArgs e)
    {
        if (_fileSystem is not null)
        {
            await _fileSystem.DisconnectAsync();
            _fileSystem = null;
        }

        btnConnect.Enabled = true;
        btnDisconnect.Enabled = false;
        lblStatus.Text = "Offline";
        tvNavigation.Nodes.Clear();
        _currentFolderItems.Clear();
        lvFiles.VirtualListSize = 0;
    }

    private async void BtnRefresh_Click(object? sender, EventArgs e)
    {
        if (_fileSystem is null)
        {
            return;
        }

        await LoadRootTreeAsync(CancellationToken.None);
    }

    private async void BtnTransfer_Click(object? sender, EventArgs e)
    {
        if (_fileSystem is null)
        {
            MessageBox.Show("Not connected to SMB.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var destination = ResolveDestinationPath();
        if (string.IsNullOrWhiteSpace(destination))
        {
            MessageBox.Show("Please select destination folder from Destination Tree or Dest textbox.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var selected = GetSelectedRemoteItems().Where(x => !x.Item.IsDirectory).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select at least one file to transfer.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        foreach (var entry in selected)
        {
            var destPath = Path.Combine(destination, entry.Item.Name);
            _transferQueue.Enqueue(_fileSystem, entry.Path, destPath, verifyChecksum: true);
        }
    }

    private void BtnCopy_Click(object? sender, EventArgs e)
    {
        SetClipboardFromSelection(ClipboardMode.Copy);
    }

    private void BtnCut_Click(object? sender, EventArgs e)
    {
        SetClipboardFromSelection(ClipboardMode.Cut);
    }

    private void BtnPaste_Click(object? sender, EventArgs e)
    {
        _ = PasteClipboardToDestinationAsync();
    }

    private async void BtnDelete_Click(object? sender, EventArgs e)
    {
        if (_fileSystem is null)
        {
            return;
        }

        var selected = GetSelectedRemoteItems();
        if (selected.Count == 0)
        {
            return;
        }

        if (MessageBox.Show("Delete selected items on SMB?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            foreach (var entry in selected)
            {
                if (entry.Item.IsDirectory)
                {
                    await _fileSystem.DeleteDirectoryAsync(entry.Path, recursive: true);
                }
                else
                {
                    await _fileSystem.DeleteFileAsync(entry.Path);
                }
            }

            await NavigateToPathAsync(tbAddress.Text, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete failed");
            MessageBox.Show($"Delete failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void BtnRename_Click(object? sender, EventArgs e)
    {
        if (_fileSystem is null)
        {
            return;
        }

        var selected = GetSelectedRemoteItems();
        if (selected.Count != 1)
        {
            MessageBox.Show("Select exactly one item for rename.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var source = selected[0];
        var newName = ShowInputDialog("Rename", "New name:", source.Item.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == source.Item.Name)
        {
            return;
        }

        var target = CombineSmbPath(tbAddress.Text, newName.Trim());

        try
        {
            await _fileSystem.RenameAsync(source.Path, target);
            await NavigateToPathAsync(tbAddress.Text, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rename failed");
            MessageBox.Show($"Rename failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void BtnNewFolder_Click(object? sender, EventArgs e)
    {
        if (_fileSystem is null)
        {
            return;
        }

        var name = ShowInputDialog("New Folder", "Folder name:", "New Folder");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var path = CombineSmbPath(tbAddress.Text, name.Trim());

        try
        {
            await _fileSystem.CreateDirectoryAsync(path);
            await NavigateToPathAsync(tbAddress.Text, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create folder failed");
            MessageBox.Show($"Create folder failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnCancelTransfer_Click(object? sender, EventArgs e)
    {
        var id = GetSelectedTransferId();
        if (id.HasValue)
        {
            _transferQueue.TryCancel(id.Value);
        }
    }

    private void BtnPauseTransfer_Click(object? sender, EventArgs e)
    {
        var id = GetSelectedTransferId();
        if (id.HasValue)
        {
            _transferQueue.TryPause(id.Value);
        }
    }

    private void BtnResumeTransfer_Click(object? sender, EventArgs e)
    {
        var id = GetSelectedTransferId();
        if (id.HasValue)
        {
            _transferQueue.TryResume(id.Value);
        }
    }

    private void BtnRetryTransfer_Click(object? sender, EventArgs e)
    {
        var id = GetSelectedTransferId();
        if (id.HasValue)
        {
            _transferQueue.TryRetry(id.Value);
        }
    }

    private void BtnTheme_Click(object? sender, EventArgs e)
    {
        var nextTheme = _themeManager.CurrentTheme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light;
        _themeManager.ApplyTheme(this, nextTheme);
    }

    private void BtnFullTest_Click(object? sender, EventArgs e)
    {
        var form = new FullTestForm(_sp, _logStore);
        form.Show(this);
    }

    private void BtnBrowseDestination_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog();
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        tbDestination.Text = dialog.SelectedPath;
        _selectedDestinationPath = dialog.SelectedPath;
    }

    private async void TvNavigation_BeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        if (_fileSystem is null)
        {
            return;
        }

        var node = e.Node;
        if (node is null)
        {
            return;
        }

        if (node.Nodes.Count == 1 && string.IsNullOrEmpty(node.Nodes[0].Text))
        {
            node.Nodes.Clear();
            await LoadTreeNodeChildrenAsync(node, node.Tag?.ToString() ?? "\\", CancellationToken.None);
        }
    }

    private async void TvNavigation_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        var node = e.Node;
        if (node is null)
        {
            return;
        }

        var path = node.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(path) || _fileSystem is null)
        {
            return;
        }

        tbAddress.Text = path;
        await NavigateToPathAsync(path, CancellationToken.None);
    }

    private void TvDestination_BeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        var node = e.Node;
        if (node is null)
        {
            return;
        }

        if (node.Nodes.Count == 1 && string.IsNullOrEmpty(node.Nodes[0].Text))
        {
            node.Nodes.Clear();
            LoadLocalChildren(node);
        }
    }

    private void TvDestination_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        SetDestinationFromSelectedNode();
    }

    private void LvFiles_RetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        if (e.ItemIndex < 0 || e.ItemIndex >= _currentFolderItems.Count)
        {
            e.Item = new ListViewItem("N/A");
            return;
        }

        var item = _currentFolderItems[e.ItemIndex];
        var lvi = new ListViewItem(item.Name);
        lvi.SubItems.Add(item.IsDirectory ? string.Empty : FormatSize(item.Size));
        lvi.SubItems.Add(item.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        lvi.SubItems.Add(item.Attributes.ToString());
        lvi.SubItems.Add(item.IsDirectory ? "Directory" : "Ready");
        e.Item = lvi;
    }

    private async void LvFiles_DoubleClick(object? sender, EventArgs e)
    {
        if (lvFiles.SelectedIndices.Count == 0)
        {
            return;
        }

        var selected = _currentFolderItems[lvFiles.SelectedIndices[0]];
        if (!selected.IsDirectory)
        {
            return;
        }

        var nextPath = CombineSmbPath(tbAddress.Text, selected.Name);
        tbAddress.Text = nextPath;
        await NavigateToPathAsync(nextPath, CancellationToken.None);
    }

    private void TransferQueue_ItemUpdated(object? sender, TransferQueueItem item)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpsertTransferRow(item));
            return;
        }

        UpsertTransferRow(item);

        if (item.State == TransferQueueState.Completed && _pendingDeleteAfterTransfer.TryGetValue(item.Id, out var sourcePath))
        {
            _pendingDeleteAfterTransfer.Remove(item.Id);
            _ = DeleteSourceAfterCutAsync(sourcePath);
        }
    }

    private void LogStore_LogAdded(object? sender, string line)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(line));
            return;
        }

        AppendLog(line);
    }

    private void UpsertTransferRow(TransferQueueItem item)
    {
        var progress = item.Progress;
        var percent = progress?.PercentComplete ?? 0;
        var speed = progress is null ? "0 MB/s" : $"{progress.SpeedBytesPerSecond / 1024d / 1024d:F2} MB/s";
        var eta = progress?.Eta?.ToString("hh\\:mm\\:ss") ?? "--:--:--";

        if (!_transferRows.TryGetValue(item.Id, out var row))
        {
            row = new ListViewItem(Path.GetFileName(item.SourcePath)) { Tag = item.Id };
            row.SubItems.Add(string.Empty);
            row.SubItems.Add(string.Empty);
            row.SubItems.Add(string.Empty);
            row.SubItems.Add(string.Empty);
            row.SubItems.Add(string.Empty);
            row.SubItems.Add(string.Empty);
            row.SubItems.Add(string.Empty);
            lvTransfers.Items.Add(row);
            _transferRows[item.Id] = row;
        }

        row.SubItems[1].Text = $"{percent:F1}%";
        row.SubItems[2].Text = speed;
        row.SubItems[3].Text = eta;
        row.SubItems[4].Text = item.RetryCount.ToString();
        row.SubItems[5].Text = item.State.ToString();
        row.SubItems[6].Text = item.SourcePath;
        row.SubItems[7].Text = item.DestinationPath;

        lblInfo.Text = $"Transfers: {lvTransfers.Items.Count} | Destination: {ResolveDestinationPath()}";
    }

    private void AppendLog(string line)
    {
        if (tbLogs.Lines.Length > 2500)
        {
            tbLogs.Clear();
        }

        tbLogs.AppendText(line + Environment.NewLine);
    }

    private Guid? GetSelectedTransferId()
    {
        if (lvTransfers.SelectedItems.Count == 0)
        {
            return null;
        }

        return lvTransfers.SelectedItems[0].Tag is Guid id ? id : null;
    }

    private async Task LoadRootTreeAsync(CancellationToken cancellationToken)
    {
        if (_fileSystem is null)
        {
            return;
        }

        tvNavigation.BeginUpdate();
        tvNavigation.Nodes.Clear();

        var rootPath = "\\";
        var root = new TreeNode(rootPath) { Tag = rootPath };
        root.Nodes.Add(new TreeNode());
        tvNavigation.Nodes.Add(root);
        tvNavigation.EndUpdate();

        await NavigateToPathAsync(rootPath, cancellationToken);
    }

    private async Task LoadTreeNodeChildrenAsync(TreeNode node, string path, CancellationToken cancellationToken)
    {
        if (_fileSystem is null)
        {
            return;
        }

        var items = await _viewModel.ListDirectoryAsync(_fileSystem, path, cancellationToken);

        foreach (var item in items.Where(x => x.IsDirectory))
        {
            var childPath = CombineSmbPath(path, item.Name);
            var child = new TreeNode(item.Name) { Tag = childPath };
            child.Nodes.Add(new TreeNode());
            node.Nodes.Add(child);
        }
    }

    private async Task NavigateToPathAsync(string path, CancellationToken cancellationToken)
    {
        if (_fileSystem is null || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var items = await _viewModel.ListDirectoryAsync(_fileSystem, path, cancellationToken);
            _currentFolderItems.Clear();
            _currentFolderItems.AddRange(items);
            lvFiles.VirtualListSize = _currentFolderItems.Count;
            lvFiles.Invalidate();
            lblInfo.Text = $"Loaded {_currentFolderItems.Count} items";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Navigate failed: {Path}", path);
            MessageBox.Show($"Cannot open path {path}: {ex.Message}", "Navigation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private List<(string Path, FileItem Item)> GetSelectedRemoteItems()
    {
        var selectedIndices = lvFiles.SelectedIndices.Cast<int>().OrderBy(i => i).ToList();
        var result = new List<(string Path, FileItem Item)>();

        foreach (var idx in selectedIndices)
        {
            if (idx < 0 || idx >= _currentFolderItems.Count)
            {
                continue;
            }

            var item = _currentFolderItems[idx];
            var fullPath = CombineSmbPath(tbAddress.Text, item.Name);
            result.Add((fullPath, item));
        }

        return result;
    }

    private void SetClipboardFromSelection(ClipboardMode mode)
    {
        var selected = GetSelectedRemoteItems();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select one or more files or folders first.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _clipboardItems.Clear();
        _clipboardItems.AddRange(selected.Select(s => (s.Path, s.Item.IsDirectory)));
        _clipboardMode = mode;

        lblInfo.Text = $"Clipboard: {_clipboardItems.Count} item(s), mode={_clipboardMode}";
    }

    private async Task PasteClipboardToDestinationAsync()
    {
        if (_fileSystem is null)
        {
            return;
        }

        if (_clipboardItems.Count == 0 || _clipboardMode == ClipboardMode.None)
        {
            MessageBox.Show("Clipboard is empty.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var destination = ResolveDestinationPath();
        if (string.IsNullOrWhiteSpace(destination))
        {
            MessageBox.Show("Please choose a destination folder.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Directory.CreateDirectory(destination);

        var skippedDirectoryCount = 0;

        foreach (var entry in _clipboardItems)
        {
            if (entry.IsDirectory)
            {
                skippedDirectoryCount++;
                continue;
            }

            var target = Path.Combine(destination, Path.GetFileName(entry.RemotePath));
            var transferId = _transferQueue.Enqueue(_fileSystem, entry.RemotePath, target, verifyChecksum: true);

            if (_clipboardMode == ClipboardMode.Cut)
            {
                _pendingDeleteAfterTransfer[transferId] = entry.RemotePath;
            }
        }

        if (skippedDirectoryCount > 0)
        {
            MessageBox.Show("Directory copy is not implemented in clipboard paste yet. Files were queued.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        if (_clipboardMode == ClipboardMode.Cut)
        {
            _clipboardItems.Clear();
            _clipboardMode = ClipboardMode.None;
        }

        await Task.CompletedTask;
    }

    private async Task DeleteSourceAfterCutAsync(string sourcePath)
    {
        if (_fileSystem is null)
        {
            return;
        }

        try
        {
            await _fileSystem.DeleteFileAsync(sourcePath);
            await NavigateToPathAsync(tbAddress.Text, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete source after cut transfer: {Path}", sourcePath);
        }
    }

    private string ResolveDestinationPath()
    {
        if (!string.IsNullOrWhiteSpace(tbDestination.Text))
        {
            _selectedDestinationPath = tbDestination.Text.Trim();
        }

        return _selectedDestinationPath;
    }

    private void LoadLocalChildren(TreeNode node)
    {
        var path = node.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                var child = new TreeNode(Path.GetFileName(dir)) { Tag = dir };
                child.Nodes.Add(new TreeNode());
                node.Nodes.Add(child);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cannot enumerate local path {Path}", path);
        }
    }

    private void SetDestinationFromSelectedNode()
    {
        var path = tvDestination.SelectedNode?.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _selectedDestinationPath = path;
        tbDestination.Text = path;
        lblInfo.Text = $"Destination set: {path}";
    }

    private void RefreshDestinationNode()
    {
        var node = tvDestination.SelectedNode;
        if (node is null)
        {
            InitializeDestinationTree();
            return;
        }

        node.Nodes.Clear();
        LoadLocalChildren(node);
    }

    private void CreateLocalFolder()
    {
        var basePath = ResolveDestinationPath();
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return;
        }

        var name = ShowInputDialog("New Folder", "Folder name:", "New Folder");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        Directory.CreateDirectory(Path.Combine(basePath, name.Trim()));
        RefreshDestinationNode();
    }

    private void RenameLocalNode()
    {
        var node = tvDestination.SelectedNode;
        var path = node?.Tag?.ToString();
        if (node is null || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var parentPath = Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return;
        }

        var newName = ShowInputDialog("Rename", "New folder name:", Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)));
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        var destPath = Path.Combine(parentPath, newName.Trim());
        Directory.Move(path, destPath);
        node.Text = newName.Trim();
        node.Tag = destPath;
        SetDestinationFromSelectedNode();
    }

    private void DeleteLocalNode()
    {
        var node = tvDestination.SelectedNode;
        var path = node?.Tag?.ToString();
        if (node is null || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (MessageBox.Show($"Delete local folder '{path}'?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        Directory.Delete(path, recursive: true);
        node.Remove();
    }

    private static string? ShowInputDialog(string title, string label, string defaultValue)
    {
        using var form = new Form
        {
            Width = 420,
            Height = 160,
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var lbl = new Label { Left = 12, Top = 14, Width = 380, Text = label };
        var txt = new TextBox { Left = 12, Top = 38, Width = 380, Text = defaultValue };
        var ok = new Button { Left = 236, Top = 72, Width = 75, Text = "OK", DialogResult = DialogResult.OK };
        var cancel = new Button { Left = 317, Top = 72, Width = 75, Text = "Cancel", DialogResult = DialogResult.Cancel };

        form.Controls.Add(lbl);
        form.Controls.Add(txt);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        return form.ShowDialog() == DialogResult.OK ? txt.Text : null;
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        _settings.Server = tbServer.Text.Trim();
        _settings.Share = tbShare.Text.Trim();
        _settings.Username = tbUsername.Text.Trim();
        _settingsManager.Save(_settings);

        _transferQueue.ItemUpdated -= TransferQueue_ItemUpdated;
        _logStore.LogAdded -= LogStore_LogAdded;

        _transferQueue.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static string FormatSize(long size)
    {
        if (size < 1024)
        {
            return size + " B";
        }

        if (size < 1024 * 1024)
        {
            return $"{size / 1024d:F2} KB";
        }

        if (size < 1024L * 1024L * 1024L)
        {
            return $"{size / 1024d / 1024d:F2} MB";
        }

        return $"{size / 1024d / 1024d / 1024d:F2} GB";
    }

    private static string CombineSmbPath(string basePath, string name)
    {
        if (string.IsNullOrWhiteSpace(basePath) || basePath == "\\")
        {
            return "\\" + name;
        }

        return basePath.TrimEnd('\\') + "\\" + name;
    }
}
