using Microsoft.Extensions.Logging;
using WorkflowManager.Models;
using WorkflowManager.Services;
using WorkflowManager.Forms;

namespace WorkflowManager;

public partial class MainForm : Form
{
    private readonly PackageScannerService _scannerService;
    private readonly PackageCopyService _copyService;
    private readonly MultiDestinationCopyService _multiCopyService;
    private readonly ILogger<MainForm> _logger;
    private List<PackageInfo> _packages = new();
    private SmbConnectionConfig _config = SmbConnectionConfig.Default;
    private List<DestinationInfo> _destinations = new();
    private string? _lastCopiedPackagePath;

    public MainForm(
        PackageScannerService scannerService,
        PackageCopyService copyService,
        MultiDestinationCopyService multiCopyService,
        ILogger<MainForm> logger)
    {
        _scannerService = scannerService;
        _copyService = copyService;
        _multiCopyService = multiCopyService;
        _logger = logger;
        InitializeComponent();
        InitializeUI();
    }

    private void InitializeUI()
    {
        // Set form properties
        Text = "Workflow Manager - Package Scanner & Copier";
        Size = new Size(1220, 750);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(245, 247, 250);
        Font = new Font("Segoe UI", 9F);
        MinimumSize = new Size(1100, 650);

        // Create main container with padding
        var mainContainer = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            BackColor = Color.FromArgb(245, 247, 250)
        };
        Controls.Add(mainContainer);

        // Title Panel
        var titlePanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            BackColor = Color.Transparent
        };
        var titleLabel = new Label
        {
            Text = "📦 Workflow Package Manager",
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            ForeColor = Color.FromArgb(44, 62, 80),
            AutoSize = true,
            Location = new Point(5, 10)
        };
        var subtitleLabel = new Label
        {
            Text = "SMB Package Scanner, Downloader & Multi-Destination Copier",
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(127, 140, 141),
            AutoSize = true,
            Location = new Point(8, 38)
        };
        titlePanel.Controls.AddRange(new Control[] { titleLabel, subtitleLabel });
        mainContainer.Controls.Add(titlePanel);

        // SMB Configuration Panel
        var configPanel = CreateConfigPanel();
        configPanel.Dock = DockStyle.Top;
        mainContainer.Controls.Add(configPanel);

        // Package List Panel
        var packagePanel = CreatePackageListPanel();
        packagePanel.Dock = DockStyle.Fill;
        mainContainer.Controls.Add(packagePanel);

        // Copy Section Panel
        var copyPanel = CreateCopyPanel();
        copyPanel.Dock = DockStyle.Bottom;
        mainContainer.Controls.Add(copyPanel);

        // Status Bar
        var statusStrip = CreateStatusStrip();
        Controls.Add(statusStrip);

        // Wire up event handlers after all controls are created
        WireUpEventHandlers();

        // Initialize default test destinations
        InitializeDefaultDestinations();
    }

    private void InitializeDefaultDestinations()
    {
        // Auto-create test destinations A1-A5 on startup for convenience
        _destinations.Clear();

        for (int i = 1; i <= 5; i++)
        {
            _destinations.Add(new DestinationInfo
            {
                Name = $"A{i}",
                UncPath = $"\\\\192.168.1.250\\share\\A{i}",
                Username = "share",
                Password = "1234567890",
                IsEnabled = true
            });
        }

        _logger.LogInformation("Default test destinations initialized: A1-A5");

        // Show multi-destination buttons since we have destinations
        var btnMultiCopy = Controls.Find("btnMultiCopy", true).FirstOrDefault() as Button;
        var btnManageDest = Controls.Find("btnManageDest", true).FirstOrDefault() as Button;
        var btnQuickTest = Controls.Find("btnQuickTest", true).FirstOrDefault() as Button;

        if (btnManageDest != null)
        {
            btnManageDest.Visible = true;
        }
    }

    private void WireUpEventHandlers()
    {
        // Find controls
        var listView = Controls.Find("lvPackages", true).FirstOrDefault() as ListView;
        var btnCopy = Controls.Find("btnCopy", true).FirstOrDefault() as Button;

        // Wire up listview selection changed
        if (listView != null && btnCopy != null)
        {
            listView.SelectedIndexChanged += (s, e) =>
            {
                btnCopy.Enabled = listView.SelectedItems.Count > 0;
            };
        }
    }

    private Panel CreateConfigPanel()
    {
        var panel = new Panel
        {
            Height = 110,
            BackColor = Color.White,
            Padding = new Padding(20),
            Margin = new Padding(0, 0, 0, 15)
        };

        // Add subtle shadow with rounded appearance
        panel.Paint += (s, e) =>
        {
            var rect = panel.ClientRectangle;
            using (var pen = new Pen(Color.FromArgb(200, 200, 210), 1))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, rect.Width - 1, rect.Height - 1);
            }
        };

        // Header
        var headerLabel = new Label
        {
            Text = "🔗 SMB Connection Settings",
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(44, 62, 80),
            AutoSize = true,
            Location = new Point(0, 0)
        };
        panel.Controls.Add(headerLabel);

        // Input fields container
        var inputY = 32;

        // Share Path
        var lblSharePath = new Label
        {
            Text = "Share:",
            Location = new Point(0, inputY + 3),
            Width = 60,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = Color.FromArgb(90, 100, 110)
        };
        var txtSharePath = new TextBox
        {
            Name = "txtSharePath",
            Location = new Point(65, inputY),
            Width = 350,
            Height = 24,
            Text = _config.SharePath,
            Font = new Font("Segoe UI", 9.5F),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(250, 250, 252)
        };
        panel.Controls.AddRange(new Control[] { lblSharePath, txtSharePath });

        // Username
        var lblUsername = new Label
        {
            Text = "User:",
            Location = new Point(430, inputY + 3),
            Width = 50,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = Color.FromArgb(90, 100, 110)
        };
        var txtUsername = new TextBox
        {
            Name = "txtUsername",
            Location = new Point(485, inputY),
            Width = 140,
            Height = 24,
            Text = _config.Username,
            Font = new Font("Segoe UI", 9.5F),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(250, 250, 252)
        };
        panel.Controls.AddRange(new Control[] { lblUsername, txtUsername });

        // Password
        var lblPassword = new Label
        {
            Text = "Pass:",
            Location = new Point(640, inputY + 3),
            Width = 45,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = Color.FromArgb(90, 100, 110)
        };
        var txtPassword = new TextBox
        {
            Name = "txtPassword",
            Location = new Point(690, inputY),
            Width = 140,
            Height = 24,
            Text = _config.Password,
            UseSystemPasswordChar = true,
            Font = new Font("Segoe UI", 9.5F),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(250, 250, 252)
        };
        panel.Controls.AddRange(new Control[] { lblPassword, txtPassword });

        // Scan Button
        var btnScan = new Button
        {
            Name = "btnScan",
            Text = "🔍 Scan",
            Location = new Point(845, inputY - 2),
            Size = new Size(100, 30),
            BackColor = Color.FromArgb(52, 152, 219),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnScan.FlatAppearance.BorderSize = 0;
        btnScan.Click += BtnScan_Click;
        panel.Controls.Add(btnScan);

        // Connection status indicator
        var lblConnectionStatus = new Label
        {
            Name = "lblConnectionStatus",
            Text = "⚫ Not Connected",
            Location = new Point(0, 68),
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Color.FromArgb(149, 165, 166)
        };
        panel.Controls.Add(lblConnectionStatus);

        return panel;
    }

    private Panel CreatePackageListPanel()
    {
        var panel = new Panel
        {
            BackColor = Color.White,
            Padding = new Padding(20),
            Margin = new Padding(0, 0, 0, 15)
        };

        panel.Paint += (s, e) =>
        {
            var rect = panel.ClientRectangle;
            using (var pen = new Pen(Color.FromArgb(200, 200, 210), 1))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, rect.Width - 1, rect.Height - 1);
            }
        };

        // Header container
        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 50,
            BackColor = Color.Transparent
        };

        var headerLabel = new Label
        {
            Text = "📋 Available Packages",
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(44, 62, 80),
            AutoSize = true,
            Location = new Point(0, 5)
        };
        headerPanel.Controls.Add(headerLabel);

        var lblPackageCount = new Label
        {
            Name = "lblPackageCount",
            Text = "No packages found",
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Color.FromArgb(127, 140, 141),
            AutoSize = true,
            Location = new Point(0, 28)
        };
        headerPanel.Controls.Add(lblPackageCount);
        panel.Controls.Add(headerPanel);

        // ListView
        var listView = new ListView
        {
            Name = "lvPackages",
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
            Font = new Font("Segoe UI", 9F),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(252, 252, 254)
        };

        // Modern column headers
        listView.Columns.Add("Folder Name", 280);
        listView.Columns.Add("Base Name", 280);
        listView.Columns.Add("Total Size", 120);
        listView.Columns.Add("Files", 80);
        listView.Columns.Add("Wxx", 70);
        listView.Columns.Add("Status", 100);

        // Custom draw for better visuals
        listView.OwnerDraw = true;
        listView.DrawColumnHeader += (s, e) =>
        {
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(44, 62, 80)), e.Bounds);
            var headerFont = new Font("Segoe UI", 9F, FontStyle.Bold);
            var textBounds = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, headerFont,
                textBounds, Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };

        listView.DrawItem += (s, e) => e.DrawDefault = true;
        listView.DrawSubItem += (s, e) => e.DrawDefault = true;

        panel.Controls.Add(listView);

        return panel;
    }

    private Panel CreateCopyPanel()
    {
        var panel = new Panel
        {
            Height = 140,
            BackColor = Color.White,
            Padding = new Padding(20)
        };

        panel.Paint += (s, e) =>
        {
            var rect = panel.ClientRectangle;
            using (var pen = new Pen(Color.FromArgb(200, 200, 210), 1))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, rect.Width - 1, rect.Height - 1);
            }
        };

        // Destination Label
        var lblDestPath = new Label
        {
            Text = "📁 Local Destination:",
            Location = new Point(0, 8),
            Width = 130,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(44, 62, 80)
        };
        panel.Controls.Add(lblDestPath);

        // Destination TextBox
        var txtDestPath = new TextBox
        {
            Name = "txtDestPath",
            Location = new Point(135, 5),
            Width = 530,
            Height = 26,
            Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Packages"),
            Font = new Font("Segoe UI", 9.5F),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(250, 250, 252)
        };
        panel.Controls.Add(txtDestPath);

        // Browse Button
        var btnBrowse = new Button
        {
            Name = "btnBrowse",
            Text = "📂",
            Location = new Point(672, 5),
            Size = new Size(40, 26),
            BackColor = Color.FromArgb(149, 165, 166),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 11F),
            Cursor = Cursors.Hand
        };
        btnBrowse.FlatAppearance.BorderSize = 0;
        btnBrowse.Click += BtnBrowse_Click;
        var browseTip = new ToolTip();
        browseTip.SetToolTip(btnBrowse, "Browse for local destination folder");
        panel.Controls.Add(btnBrowse);

        // Copy Button
        var btnCopy = new Button
        {
            Name = "btnCopy",
            Text = "🚀 Start Unified Workflow",
            Location = new Point(725, 3),
            Size = new Size(230, 30),
            BackColor = Color.FromArgb(46, 204, 113),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Enabled = false
        };
        btnCopy.FlatAppearance.BorderSize = 0;
        btnCopy.Click += BtnUnifiedWorkflow_Click;
        panel.Controls.Add(btnCopy);

        // Separator line
        var separator = new Label
        {
            Location = new Point(0, 42),
            Size = new Size(panel.Width - 40, 1),
            BackColor = Color.FromArgb(220, 220, 230),
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        panel.Controls.Add(separator);

        // Advanced options label
        var advLabel = new Label
        {
            Name = "lblAdvOptions",
            Text = "Multi-Destination Options: (5 destinations configured)",
            Location = new Point(0, 52),
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(127, 140, 141)
        };
        panel.Controls.Add(advLabel);

        // Multi-Destination Copy Button (initially hidden)
        var btnMultiCopy = new Button
        {
            Name = "btnMultiCopy",
            Text = "📤 Copy to Multiple Destinations",
            Location = new Point(0, 75),
            Size = new Size(250, 32),
            BackColor = Color.FromArgb(52, 152, 219),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Visible = false,
            Enabled = false
        };
        btnMultiCopy.FlatAppearance.BorderSize = 0;
        btnMultiCopy.Click += BtnMultiCopy_Click;
        panel.Controls.Add(btnMultiCopy);

        // Manage Destinations Button
        var btnManageDest = new Button
        {
            Name = "btnManageDest",
            Text = "⚙️ Manage Destinations (5)",
            Location = new Point(260, 75),
            Size = new Size(200, 32),
            BackColor = Color.FromArgb(149, 165, 166),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9F),
            Cursor = Cursors.Hand,
            Visible = false
        };
        btnManageDest.FlatAppearance.BorderSize = 0;
        btnManageDest.Click += BtnManageDest_Click;
        panel.Controls.Add(btnManageDest);

        return panel;
    }

    private StatusStrip CreateStatusStrip()
    {
        var statusStrip = new StatusStrip
        {
            BackColor = Color.FromArgb(52, 73, 94),
            Font = new Font("Segoe UI", 9F)
        };

        var lblStatus = new ToolStripStatusLabel
        {
            Name = "lblStatus",
            Text = "Ready",
            ForeColor = Color.White,
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        statusStrip.Items.Add(lblStatus);

        return statusStrip;
    }

    private async void BtnScan_Click(object? sender, EventArgs e)
    {
        var txtSharePath = Controls.Find("txtSharePath", true).FirstOrDefault() as TextBox;
        var txtUsername = Controls.Find("txtUsername", true).FirstOrDefault() as TextBox;
        var txtPassword = Controls.Find("txtPassword", true).FirstOrDefault() as TextBox;
        var btnScan = Controls.Find("btnScan", true).FirstOrDefault() as Button;
        var statusStrip = Controls.OfType<StatusStrip>().FirstOrDefault();
        var lblStatus = statusStrip?.Items["lblStatus"] as ToolStripStatusLabel;
        var listView = Controls.Find("lvPackages", true).FirstOrDefault() as ListView;
        var lblConnectionStatus = Controls.Find("lblConnectionStatus", true).FirstOrDefault() as Label;
        var lblPackageCount = Controls.Find("lblPackageCount", true).FirstOrDefault() as Label;

        if (txtSharePath == null || txtUsername == null || txtPassword == null || 
            btnScan == null || lblStatus == null || listView == null)
            return;

        try
        {
            btnScan.Enabled = false;
            btnScan.Text = "⏳ Scanning...";
            lblStatus.Text = "🔄 Connecting to SMB share...";
            if (lblConnectionStatus != null)
                lblConnectionStatus.Text = "🟡 Connecting...";

            listView.Items.Clear();

            _config = new SmbConnectionConfig
            {
                SharePath = txtSharePath.Text,
                Username = txtUsername.Text,
                Password = txtPassword.Text
            };

            // Setup progress reporting
            var progress = new Progress<ScanProgress>(p =>
            {
                lblStatus.Text = $"🔍 {p.Status} (Scanned: {p.ScannedDirectories} dirs, Found: {p.FoundPackages} packages)";

                if (lblPackageCount != null)
                    lblPackageCount.Text = $"Scanning... {p.FoundPackages} packages found so far";
            });

            _packages = await _scannerService.ScanPackagesAsync(_config, progress);

            // Update connection status
            if (lblConnectionStatus != null)
                lblConnectionStatus.Text = "🟢 Connected";

            foreach (var package in _packages)
            {
                var item = new ListViewItem(package.FolderName)
                {
                    Font = new Font("Segoe UI", 9F)
                };
                item.SubItems.Add(package.BaseName);
                item.SubItems.Add(FormatFileSize(package.TotalSize));
                item.SubItems.Add(package.FileCount.ToString());
                item.SubItems.Add(package.WxxFileCount.ToString());
                item.SubItems.Add(package.IsValid ? "✅ Valid" : "❌ Invalid");
                item.Tag = package;

                if (package.IsValid)
                {
                    item.BackColor = Color.FromArgb(230, 247, 235);
                    item.ForeColor = Color.FromArgb(39, 174, 96);
                }
                else
                {
                    item.BackColor = Color.FromArgb(252, 236, 236);
                    item.ForeColor = Color.FromArgb(192, 57, 43);
                }

                listView.Items.Add(item);
            }

            var validCount = _packages.Count(p => p.IsValid);
            var invalidCount = _packages.Count - validCount;

            if (lblPackageCount != null)
                lblPackageCount.Text = $"Found {_packages.Count} packages ({validCount} valid, {invalidCount} invalid)";

            lblStatus.Text = $"✅ Scan completed: {_packages.Count} packages found ({validCount} valid)";
        }
        catch (Exception ex)
        {
            if (lblConnectionStatus != null)
                lblConnectionStatus.Text = "🔴 Connection Failed";

            lblStatus.Text = $"❌ Error: {ex.Message}";
            MessageBox.Show($"Error scanning packages:\n\n{ex.Message}", "Scan Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            _logger.LogError(ex, "Error during scan");
        }
        finally
        {
            btnScan.Enabled = true;
            btnScan.Text = "🔍 Scan Packages";
        }
    }

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        var txtDestPath = Controls.Find("txtDestPath", true).FirstOrDefault() as TextBox;
        if (txtDestPath == null) return;

        using var dialog = new FolderBrowserDialog
        {
            Description = "Select destination folder for packages",
            SelectedPath = txtDestPath.Text,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            txtDestPath.Text = dialog.SelectedPath;
        }
    }

    private async void BtnCopy_Click(object? sender, EventArgs e)
    {
        var listView = Controls.Find("lvPackages", true).FirstOrDefault() as ListView;
        var txtDestPath = Controls.Find("txtDestPath", true).FirstOrDefault() as TextBox;
        var btnCopy = Controls.Find("btnCopy", true).FirstOrDefault() as Button;
        var btnScan = Controls.Find("btnScan", true).FirstOrDefault() as Button;
        var statusStrip = Controls.OfType<StatusStrip>().FirstOrDefault();
        var lblStatus = statusStrip?.Items["lblStatus"] as ToolStripStatusLabel;

        if (listView == null || txtDestPath == null || btnCopy == null || 
            btnScan == null || lblStatus == null)
            return;

        if (listView.SelectedItems.Count == 0) return;

        var selectedPackage = listView.SelectedItems[0].Tag as PackageInfo;
        if (selectedPackage == null) return;

        if (!selectedPackage.IsValid)
        {
            MessageBox.Show("Selected package is not valid!", "Invalid Package", 
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var destPath = txtDestPath.Text;
        if (string.IsNullOrWhiteSpace(destPath))
        {
            MessageBox.Show("Please specify destination path!", "Missing Destination", 
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            btnCopy.Enabled = false;
            btnScan.Enabled = false;
            lblStatus.Text = "🔄 Initializing copy operation...";

            // Create and show progress dialog
            using var progressDialog = new CopyProgressDialog(selectedPackage.FolderName, selectedPackage.FileCount);

            // Track which files have been counted as complete (to avoid double-counting)
            var completedFileNames = new HashSet<string>();
            var addedFileNames = new HashSet<string>(); // Track files already added to dialog
            var lockObj = new object();

            // Start copy operation in background
            var copyTask = Task.Run(async () =>
            {
                var progress = new Progress<FileCopyInfo>(fileInfo =>
                {
                    // Add file to dialog only once, when first report comes in
                    lock (lockObj)
                    {
                        if (addedFileNames.Add(fileInfo.FileName))
                        {
                            progressDialog.AddFile(fileInfo);
                        }
                    }

                    // Always update file progress on UI thread via Progress<T>
                    progressDialog.UpdateFileProgress(fileInfo.FileName, fileInfo);

                    // Count completed files only once per file
                    var isTerminalStatus = fileInfo.Status == FileCopyStatus.Verified || 
                                          fileInfo.Status == FileCopyStatus.CopyComplete ||
                                          fileInfo.Status == FileCopyStatus.Failed ||
                                          fileInfo.Status == FileCopyStatus.HashMismatch;

                    if (isTerminalStatus)
                    {
                        lock (lockObj)
                        {
                            if (completedFileNames.Add(fileInfo.FileName))
                            {
                                progressDialog.UpdateOverallProgress(completedFileNames.Count, selectedPackage.FileCount);
                            }
                        }
                    }

                    progressDialog.UpdateStatus($"Processing: {fileInfo.FileName}");
                });

                return await _copyService.CopyPackageWithVerificationAsync(
                    _config, 
                    selectedPackage, 
                    destPath, 
                    progress);
            });

            // Show dialog and wait for completion or cancellation
            progressDialog.Show(this);

            CopyResult? result = null;
            while (!copyTask.IsCompleted)
            {
                Application.DoEvents();
                await Task.Delay(100);

                if (progressDialog.IsCancelled)
                {
                    lblStatus.Text = "⏸️ Copy cancelled by user";
                    return;
                }
            }

            result = await copyTask;

            // Show completion status
            var statusIcon = result.Success ? "✅" : "❌";
            var statusMessage = result.Success
                ? $"{statusIcon} Successfully copied and verified {result.VerifiedFiles}/{result.TotalFiles} files in {result.Duration.TotalSeconds:F1}s"
                : $"{statusIcon} Copy completed with {result.FailedFiles} failed files";

            progressDialog.SetComplete(result.Success, statusMessage);
            lblStatus.Text = statusMessage;

            // Let user review the dialog before closing
            while (progressDialog.Visible)
            {
                Application.DoEvents();
                await Task.Delay(100);
            }

            if (result.Success)
            {
                // Store the local package path for multi-destination copy
                _lastCopiedPackagePath = Path.Combine(destPath, selectedPackage.FolderName);

                // Show multi-destination copy buttons
                var btnMultiCopy = Controls.Find("btnMultiCopy", true).FirstOrDefault() as Button;
                var btnManageDest = Controls.Find("btnManageDest", true).FirstOrDefault() as Button;

                if (btnMultiCopy != null)
                {
                    btnMultiCopy.Visible = true;
                    btnMultiCopy.Enabled = true;
                }

                if (btnManageDest != null)
                {
                    btnManageDest.Visible = true;
                }

                MessageBox.Show(
                    $"Package copied and verified successfully!\n\n" +
                    $"Files: {result.VerifiedFiles}/{result.TotalFiles}\n" +
                    $"Duration: {result.Duration.TotalSeconds:F1}s\n" +
                    $"Destination: {_lastCopiedPackagePath}\n\n" +
                    $"ℹ️ You can now copy this package to multiple destinations.",
                    "Copy Completed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(
                    $"Copy completed with issues!\n\n" +
                    $"Verified: {result.VerifiedFiles}/{result.TotalFiles}\n" +
                    $"Failed: {result.FailedFiles}\n" +
                    $"Error: {result.ErrorMessage ?? "See log for details"}",
                    "Copy Issues",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"❌ Copy failed: {ex.Message}";
            MessageBox.Show($"Error copying package:\n\n{ex.Message}", "Copy Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            _logger.LogError(ex, "Error during copy");
        }
        finally
        {
            btnCopy.Enabled = true;
            btnScan.Enabled = true;
        }
    }

    private void BtnUnifiedWorkflow_Click(object? sender, EventArgs e)
    {
        var listView = Controls.Find("lvPackages", true).FirstOrDefault() as ListView;
        var txtDestPath = Controls.Find("txtDestPath", true).FirstOrDefault() as TextBox;
        var statusStrip = Controls.OfType<StatusStrip>().FirstOrDefault();
        var lblStatus = statusStrip?.Items["lblStatus"] as ToolStripStatusLabel;

        if (listView == null || txtDestPath == null || lblStatus == null)
            return;

        if (listView.SelectedItems.Count == 0)
        {
            MessageBox.Show("Please select a package first.", "No Selection",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selectedPackage = listView.SelectedItems[0].Tag as PackageInfo;
        if (selectedPackage == null) return;

        if (!selectedPackage.IsValid)
        {
            MessageBox.Show("Selected package is not valid!", "Invalid Package",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var destPath = txtDestPath.Text;
        if (string.IsNullOrWhiteSpace(destPath))
        {
            MessageBox.Show("Please select a destination folder!", "No Destination",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            lblStatus.Text = "🚀 Opening unified workflow...";

            // Create unified workflow dialog with all services
            using var unifiedDialog = new UnifiedWorkflowDialog(
                _scannerService,
                _copyService,
                _multiCopyService,
                _config,
                selectedPackage,
                destPath,
                _destinations);

            var result = unifiedDialog.ShowDialog(this);

            if (result == DialogResult.OK)
            {
                // Store the copied package path for later use
                _lastCopiedPackagePath = Path.Combine(destPath, selectedPackage.FolderName);

                // Show additional buttons
                var btnMultiCopy = Controls.Find("btnMultiCopy", true).FirstOrDefault() as Button;
                var btnManageDest = Controls.Find("btnManageDest", true).FirstOrDefault() as Button;

                if (btnMultiCopy != null)
                {
                    btnMultiCopy.Visible = true;
                    btnMultiCopy.Enabled = true;
                }

                if (btnManageDest != null)
                {
                    btnManageDest.Visible = true;
                }

                lblStatus.Text = "✅ Workflow completed!";
            }
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"❌ Error: {ex.Message}";
            MessageBox.Show($"Error starting workflow:\n\n{ex.Message}", "Workflow Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            _logger.LogError(ex, "Error starting unified workflow");
        }
    }

    private async void BtnCopyWorkflow_Click(object? sender, EventArgs e)
    {
        var listView = Controls.Find("lvPackages", true).FirstOrDefault() as ListView;
        var txtDestPath = Controls.Find("txtDestPath", true).FirstOrDefault() as TextBox;
        var btnCopy = Controls.Find("btnCopy", true).FirstOrDefault() as Button;
        var btnScan = Controls.Find("btnScan", true).FirstOrDefault() as Button;
        var statusStrip = Controls.OfType<StatusStrip>().FirstOrDefault();
        var lblStatus = statusStrip?.Items["lblStatus"] as ToolStripStatusLabel;

        if (listView == null || txtDestPath == null || btnCopy == null || 
            btnScan == null || lblStatus == null)
            return;

        if (listView.SelectedItems.Count == 0) return;

        var selectedPackage = listView.SelectedItems[0].Tag as PackageInfo;
        if (selectedPackage == null) return;

        if (!selectedPackage.IsValid)
        {
            MessageBox.Show("Selected package is not valid!", "Invalid Package", 
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var destPath = txtDestPath.Text;
        if (string.IsNullOrWhiteSpace(destPath))
        {
            MessageBox.Show("Please select a destination folder!", "No Destination", 
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            btnCopy.Enabled = false;
            btnScan.Enabled = false;
            lblStatus.Text = "🔄 Starting workflow...";

            // Create workflow dialog
            using var workflowDialog = new WorkflowProgressDialog(selectedPackage.FolderName);

            // Start workflow in background
            var workflowTask = Task.Run(async () =>
            {
                // Step 1: Download from SMB
                workflowDialog.StartDownload(selectedPackage.FileCount);

                var downloadProgress = new Progress<FileCopyInfo>(fileInfo =>
                {
                    workflowDialog.AddDownloadFile(fileInfo);
                    workflowDialog.UpdateDownloadProgress(fileInfo);
                });

                var copyResult = await _copyService.CopyPackageWithVerificationAsync(
                    _config,
                    selectedPackage,
                    destPath,
                    downloadProgress);

                var downloadSuccess = copyResult.Success;
                var downloadMessage = downloadSuccess
                    ? $"Downloaded and verified {copyResult.VerifiedFiles}/{copyResult.TotalFiles} files"
                    : $"Download completed with {copyResult.FailedFiles} failures";

                workflowDialog.CompleteDownload(downloadSuccess, downloadMessage);

                // Step 2 is integrated in Step 1 (verification happens during download)
                // But we show it separately in the tree
                workflowDialog.StartVerification(selectedPackage.FileCount);

                foreach (var fileInfo in copyResult.FilesProcessed)
                {
                    workflowDialog.AddVerifyFile(fileInfo);
                    workflowDialog.UpdateVerifyProgress(fileInfo);
                }

                var verifySuccess = copyResult.VerifiedFiles == copyResult.TotalFiles;
                var verifyMessage = verifySuccess
                    ? $"All {copyResult.VerifiedFiles} files verified"
                    : $"{copyResult.VerifiedFiles} verified, {copyResult.FailedFiles} failed";

                workflowDialog.CompleteVerification(verifySuccess || copyResult.VerifiedFiles > 0, verifyMessage);

                if (!downloadSuccess)
                {
                    return copyResult;
                }

                // Store for multi-destination copy
                _lastCopiedPackagePath = Path.Combine(destPath, selectedPackage.FolderName);

                return copyResult;
            });

            // Show dialog (non-blocking)
            workflowDialog.Show(this);

            var result = await workflowTask;

            // Step 3: Ask for multi-destination copy (on UI thread)
            if (result.Success)
            {
                var enabledDests = _destinations.Where(d => d.IsEnabled).ToList();
                if (enabledDests.Count > 0)
                {
                    var askResult = MessageBox.Show(
                        $"Download completed successfully!\n\n" +
                        $"Copy to {enabledDests.Count} destination(s)?\n" +
                        string.Join("\n", enabledDests.Select(d => $"• {d.Name}: {d.UncPath}")),
                        "Multi-Destination Copy",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (askResult == DialogResult.Yes)
                    {
                        workflowDialog.StartMultiCopy(enabledDests);

                        var multiProgress = new Progress<List<DestinationCopyResult>>(results =>
                        {
                            workflowDialog.UpdateMultiCopyProgress(results);
                        });

                        await _multiCopyService.CopyToMultipleDestinationsAsync(
                            _lastCopiedPackagePath,
                            selectedPackage.FolderName,
                            enabledDests,
                            multiProgress);

                        // Show completion message in dialog
                        MessageBox.Show(
                            $"✅ Multi-destination copy completed!\n\n" +
                            $"Total destinations: {enabledDests.Count}\n" +
                            $"Check the workflow tree for details.",
                            "Workflow Complete",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }
                else
                {
                    // No destinations configured - show message
                    var setupResult = MessageBox.Show(
                        "Download and verification completed!\n\n" +
                        "No destinations configured for Step 3.\n\n" +
                        "Would you like to setup test destinations now?",
                        "Setup Test Destinations",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (setupResult == DialogResult.Yes)
                    {
                        // Open destination manager with quick test setup
                        using var destMgr = new DestinationManagerDialog(_destinations);
                        if (destMgr.ShowDialog() == DialogResult.OK)
                        {
                            _destinations = destMgr.Destinations;

                            // Ask if user wants to copy now
                            var copyNowResult = MessageBox.Show(
                                $"Destinations configured!\n\n" +
                                $"Copy to {_destinations.Count(d => d.IsEnabled)} destination(s) now?",
                                "Copy Now",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Question);

                            if (copyNowResult == DialogResult.Yes)
                            {
                                var newEnabledDests = _destinations.Where(d => d.IsEnabled).ToList();
                                workflowDialog.StartMultiCopy(newEnabledDests);

                                var multiProgress = new Progress<List<DestinationCopyResult>>(results =>
                                {
                                    workflowDialog.UpdateMultiCopyProgress(results);
                                });

                                await _multiCopyService.CopyToMultipleDestinationsAsync(
                                    _lastCopiedPackagePath,
                                    selectedPackage.FolderName,
                                    newEnabledDests,
                                    multiProgress);
                            }
                        }
                    }
                }
            }

            // Don't close dialog automatically - let user review and close
            // workflowDialog.Close(); // Removed - user will close manually

            // Show multi-copy buttons if download was successful
            if (result.Success)
            {
                var btnMultiCopy = Controls.Find("btnMultiCopy", true).FirstOrDefault() as Button;
                var btnManageDest = Controls.Find("btnManageDest", true).FirstOrDefault() as Button;

                if (btnMultiCopy != null)
                {
                    btnMultiCopy.Visible = true;
                    btnMultiCopy.Enabled = true;
                }

                if (btnManageDest != null)
                {
                    btnManageDest.Visible = true;
                }

                lblStatus.Text = "✅ Workflow completed!";
            }
            else
            {
                lblStatus.Text = "⚠️ Workflow completed with errors";
            }
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"❌ Workflow failed: {ex.Message}";
            MessageBox.Show($"Error during workflow:\n\n{ex.Message}", "Workflow Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            _logger.LogError(ex, "Error during workflow");
        }
        finally
        {
            btnCopy.Enabled = true;
            btnScan.Enabled = true;
        }
    }

    private void BtnManageDest_Click(object? sender, EventArgs e)
    {
        using var dialog = new DestinationManagerDialog(_destinations);
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _destinations = dialog.Destinations;
            _logger.LogInformation("Destinations updated: {Count} destinations configured", _destinations.Count);

            var btnMultiCopy = Controls.Find("btnMultiCopy", true).FirstOrDefault() as Button;
            if (btnMultiCopy != null)
            {
                btnMultiCopy.Enabled = _destinations.Any(d => d.IsEnabled) && !string.IsNullOrEmpty(_lastCopiedPackagePath);
            }
        }
    }

    private async void BtnQuickTest_Click(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            "⚡ Quick Test Setup\n\n" +
            "This will create 5 test destinations:\n" +
            "• A1: \\\\192.168.1.250\\share\\A1\n" +
            "• A2: \\\\192.168.1.250\\share\\A2\n" +
            "• A3: \\\\192.168.1.250\\share\\A3\n" +
            "• A4: \\\\192.168.1.250\\share\\A4\n" +
            "• A5: \\\\192.168.1.250\\share\\A5\n\n" +
            "⚠️ Make sure these folders exist on the SMB server!\n\n" +
            "Continue?",
            "Quick Test Setup",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
            return;

        // Generate test destinations
        _destinations.Clear();
        for (int i = 1; i <= 5; i++)
        {
            _destinations.Add(new DestinationInfo
            {
                Name = $"A{i}",
                UncPath = $"\\\\192.168.1.250\\share\\A{i}",
                Username = "share",
                Password = "1234567890",
                IsEnabled = true
            });
        }

        _logger.LogInformation("Quick test destinations created: 5 destinations (A1-A5)");

        // Show buttons
        var btnMultiCopy = Controls.Find("btnMultiCopy", true).FirstOrDefault() as Button;
        var btnManageDest = Controls.Find("btnManageDest", true).FirstOrDefault() as Button;

        if (btnMultiCopy != null)
        {
            btnMultiCopy.Visible = true;
            btnMultiCopy.Enabled = !string.IsNullOrEmpty(_lastCopiedPackagePath);
        }

        if (btnManageDest != null)
        {
            btnManageDest.Visible = true;
        }

        MessageBox.Show(
            "✅ Quick test destinations configured!\n\n" +
            $"• 5 destinations created (A1-A5)\n" +
            $"• All enabled and ready\n\n" +
            $"You can now copy to multiple destinations.",
            "Success",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private async void BtnMultiCopy_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_lastCopiedPackagePath) || !Directory.Exists(_lastCopiedPackagePath))
        {
            MessageBox.Show(
                "No valid local package found.\nPlease copy a package from SMB first.",
                "Multi-Destination Copy",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var enabledDestinations = _destinations.Where(d => d.IsEnabled).ToList();
        if (enabledDestinations.Count == 0)
        {
            MessageBox.Show(
                "No destinations configured or enabled.\nPlease configure destinations first.",
                "Multi-Destination Copy",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var packageName = Path.GetFileName(_lastCopiedPackagePath);
        var result = MessageBox.Show(
            $"Copy package '{packageName}' to {enabledDestinations.Count} destination(s)?\n\n" +
            string.Join("\n", enabledDestinations.Select(d => $"• {d.Name}: {d.UncPath}")),
            "Confirm Multi-Destination Copy",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
            return;

        var btnMultiCopy = sender as Button;
        if (btnMultiCopy != null)
        {
            btnMultiCopy.Enabled = false;
        }

        try
        {
            using var progressDialog = new MultiDestinationCopyDialog(packageName, enabledDestinations);

            var progress = new Progress<List<DestinationCopyResult>>(results =>
            {
                progressDialog.UpdateProgress(results);
            });

            var copyTask = Task.Run(async () =>
            {
                return await _multiCopyService.CopyToMultipleDestinationsAsync(
                    _lastCopiedPackagePath,
                    packageName,
                    enabledDestinations,
                    progress);
            });

            progressDialog.ShowDialog(this);

            var results = await copyTask;

            var successCount = results.Count(r => r.Status == DestinationStatus.Completed);
            var errorCount = results.Count(r => r.Status == DestinationStatus.CompletedWithErrors);
            var failedCount = results.Count(r => r.Status == DestinationStatus.Failed);

            var message = $"Multi-destination copy completed!\n\n" +
                         $"✅ Successful: {successCount}\n" +
                         $"⚠️ With errors: {errorCount}\n" +
                         $"❌ Failed: {failedCount}";

            var icon = failedCount > 0 ? MessageBoxIcon.Error :
                      errorCount > 0 ? MessageBoxIcon.Warning :
                      MessageBoxIcon.Information;

            MessageBox.Show(message, "Multi-Destination Copy Complete", MessageBoxButtons.OK, icon);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error during multi-destination copy:\n\n{ex.Message}",
                "Multi-Copy Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            _logger.LogError(ex, "Error during multi-destination copy");
        }
        finally
        {
            if (btnMultiCopy != null)
            {
                btnMultiCopy.Enabled = true;
            }
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

