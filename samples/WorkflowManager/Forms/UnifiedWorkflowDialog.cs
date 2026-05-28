using WorkflowManager.Models;
using WorkflowManager.Services;
using System.Diagnostics;

namespace WorkflowManager.Forms;

/// <summary>
/// Unified Workflow Dialog - Gộp toàn bộ workflow vào 1 cửa sổ duy nhất
/// Hiển thị dạng tree với từng step có nút riêng
/// </summary>
public class UnifiedWorkflowDialog : Form
{
    private readonly PackageScannerService _scannerService;
    private readonly PackageCopyService _copyService;
    private readonly MultiDestinationCopyService _multiCopyService;
    private readonly SmbConnectionConfig _config;
    private readonly PackageInfo _package;
    private readonly string _localDestPath;
    private readonly List<DestinationInfo> _destinations;

    // UI Controls
    private Panel _step1Panel = null!;
    private Panel _step2Panel = null!;
    private Panel _step3Panel = null!;
    private TreeView _treeView = null!;
    private Label _lblStatus = null!;
    private ProgressBar _progressBar = null!;
    private Button _btnStep1 = null!;
    private Button _btnStep2 = null!;
    private Button _btnStep3 = null!;
    private Button _btnClose = null!;

    // State
    private string? _copiedPackagePath;
    private Dictionary<string, TreeNode> _fileNodes = new();
    private List<DestinationCopyResult> _multiCopyResults = new();
    private CancellationTokenSource? _cts;

    public UnifiedWorkflowDialog(
        PackageScannerService scannerService,
        PackageCopyService copyService,
        MultiDestinationCopyService multiCopyService,
        SmbConnectionConfig config,
        PackageInfo package,
        string localDestPath,
        List<DestinationInfo> destinations)
    {
        _scannerService = scannerService;
        _copyService = copyService;
        _multiCopyService = multiCopyService;
        _config = config;
        _package = package;
        _localDestPath = localDestPath;
        _destinations = destinations;

        InitializeUI();
    }

    private void InitializeUI()
    {
        Text = $"📦 Workflow: {_package.FolderName}";
        Size = new Size(1600, 900);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1400, 700);
        BackColor = Color.FromArgb(245, 245, 250);
        FormBorderStyle = FormBorderStyle.Sizable;

        // Title Panel
        var titlePanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 80,
            BackColor = Color.FromArgb(52, 73, 94),
            Padding = new Padding(20)
        };

        var lblTitle = new Label
        {
            Text = $"Package Workflow: {_package.FolderName}",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(20, 15)
        };
        titlePanel.Controls.Add(lblTitle);

        var lblSubtitle = new Label
        {
            Text = $"📁 Source: {_config.SharePath} → 💾 Local: {_localDestPath}",
            ForeColor = Color.FromArgb(189, 195, 199),
            Font = new Font("Segoe UI", 9F),
            AutoSize = true,
            Location = new Point(20, 45)
        };
        titlePanel.Controls.Add(lblSubtitle);

        Controls.Add(titlePanel);

        // Step Buttons Panel
        var stepPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 70,
            BackColor = Color.White,
            Padding = new Padding(20, 15, 20, 15)
        };

        _btnStep1 = CreateStepButton("1️⃣ Download", 0, true);
        _btnStep1.Click += BtnStep1_Click;

        _btnStep2 = CreateStepButton("2️⃣ Verify", 250, false);
        _btnStep2.Click += BtnStep2_Click;

        _btnStep3 = CreateStepButton("3️⃣ Copy to Dests", 500, false);
        _btnStep3.Click += BtnStep3_Click;

        stepPanel.Controls.AddRange(new Control[] { _btnStep1, _btnStep2, _btnStep3 });

        // Status Panel (Bottom)
        var statusPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 110,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            Padding = new Padding(15)
        };

        _lblStatus = new Label
        {
            Text = "Ready to start workflow...",
            Font = new Font("Segoe UI", 10F),
            AutoSize = false,
            Size = new Size(statusPanel.Width - 150, 25),
            Location = new Point(15, 15),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        statusPanel.Controls.Add(_lblStatus);

        _progressBar = new ProgressBar
        {
            Location = new Point(15, 50),
            Size = new Size(statusPanel.Width - 150, 25),
            Style = ProgressBarStyle.Continuous,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        statusPanel.Controls.Add(_progressBar);

        _btnClose = new Button
        {
            Text = "Close",
            Location = new Point(statusPanel.Width - 120, 50),
            Size = new Size(100, 35),
            BackColor = Color.FromArgb(149, 165, 166),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.OK
        };
        _btnClose.FlatAppearance.BorderSize = 0;
        statusPanel.Controls.Add(_btnClose);

        // Main Content Panel (Fill remaining space)
        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            BackColor = Color.FromArgb(245, 245, 250)
        };

        // TreeView (Fill content panel)
        _treeView = new TreeView
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F),
            ShowLines = true,
            ShowPlusMinus = true,
            ShowRootLines = true,
            FullRowSelect = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White
        };

        contentPanel.Controls.Add(_treeView);

        // Add controls in correct order for proper docking
        Controls.Add(statusPanel);  // Bottom first
        Controls.Add(contentPanel); // Fill second
        Controls.Add(stepPanel);    // Top third (will be on top)
        Controls.Add(titlePanel);   // Top last (will be on very top)

        InitializeTreeView();
    }

    private Button CreateStepButton(string text, int x, bool enabled)
    {
        var btn = new Button
        {
            Text = text,
            Location = new Point(x, 0),
            Size = new Size(240, 40),
            BackColor = enabled ? Color.FromArgb(52, 152, 219) : Color.FromArgb(189, 195, 199),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Cursor = enabled ? Cursors.Hand : Cursors.Default,
            Enabled = enabled
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    private void InitializeTreeView()
    {
        _treeView.Nodes.Clear();

        // Root node - Package
        var rootNode = new TreeNode($"📦 {_package.FolderName}")
        {
            Tag = new NodeData { Type = NodeType.Package }
        };
        _treeView.Nodes.Add(rootNode);

        // Step 1 node
        var step1Node = new TreeNode("⏸️ Step 1: Download from SMB (Not started)")
        {
            Tag = new NodeData { Type = NodeType.Step, StepNumber = 1 }
        };
        rootNode.Nodes.Add(step1Node);

        // Step 2 node
        var step2Node = new TreeNode("⏸️ Step 2: Verify Checksums (Not started)")
        {
            Tag = new NodeData { Type = NodeType.Step, StepNumber = 2 }
        };
        rootNode.Nodes.Add(step2Node);

        // Step 3 node
        var step3Node = new TreeNode("⏸️ Step 3: Copy to Multiple Destinations (Not started)")
        {
            Tag = new NodeData { Type = NodeType.Step, StepNumber = 3 }
        };
        rootNode.Nodes.Add(step3Node);

        _treeView.ExpandAll();
    }

    #region Step 1: Download from SMB

    private async void BtnStep1_Click(object? sender, EventArgs e)
    {
        _btnStep1.Enabled = false;
        _cts = new CancellationTokenSource();

        try
        {
            var step1Node = FindStepNode(1);
            if (step1Node == null) return;

            step1Node.Text = "⏳ Step 1: Download from SMB (In progress...)";
            step1Node.ForeColor = Color.Blue;
            step1Node.Nodes.Clear();

            _lblStatus.Text = "🔄 Downloading files from SMB...";
            _progressBar.Value = 0;
            _progressBar.Maximum = _package.FileCount;

            var progress = new Progress<FileCopyInfo>(fileInfo =>
            {
                if (InvokeRequired)
                {
                    Invoke(() => UpdateDownloadProgress(step1Node, fileInfo));
                }
                else
                {
                    UpdateDownloadProgress(step1Node, fileInfo);
                }
            });

            var result = await _copyService.CopyPackageWithVerificationAsync(
                _config,
                _package,
                _localDestPath,
                progress,
                _cts.Token);

            _copiedPackagePath = Path.Combine(_localDestPath, _package.FolderName);

            if (result.Success)
            {
                step1Node.Text = $"✅ Step 1: Download from SMB (Complete - {result.VerifiedFiles}/{result.TotalFiles} verified)";
                step1Node.ForeColor = Color.Green;
                _lblStatus.Text = $"✅ Step 1 complete! {result.VerifiedFiles}/{result.TotalFiles} files downloaded and verified.";

                // Enable Step 2
                _btnStep2.Enabled = true;
                _btnStep2.BackColor = Color.FromArgb(52, 152, 219);
                _btnStep2.Cursor = Cursors.Hand;
            }
            else
            {
                step1Node.Text = $"⚠️ Step 1: Download from SMB (Completed with errors - {result.FailedFiles} failed)";
                step1Node.ForeColor = Color.Orange;
                _lblStatus.Text = $"⚠️ Step 1 completed with {result.FailedFiles} errors.";
            }
        }
        catch (Exception ex)
        {
            var step1Node = FindStepNode(1);
            if (step1Node != null)
            {
                step1Node.Text = $"❌ Step 1: Download from SMB (Failed)";
                step1Node.ForeColor = Color.Red;
            }

            _lblStatus.Text = $"❌ Error: {ex.Message}";
            MessageBox.Show($"Error during download:\n\n{ex.Message}", "Step 1 Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void UpdateDownloadProgress(TreeNode parentNode, FileCopyInfo fileInfo)
    {
        var fileName = fileInfo.FileName;

        if (!_fileNodes.ContainsKey(fileName))
        {
            var fileNode = new TreeNode($"📄 {fileName}")
            {
                Tag = new NodeData { Type = NodeType.File, FileInfo = fileInfo }
            };
            parentNode.Nodes.Add(fileNode);
            _fileNodes[fileName] = fileNode;
        }

        var node = _fileNodes[fileName];
        var status = fileInfo.Status switch
        {
            FileCopyStatus.Copying => $"📄 {fileName} - ⏳ Copying ({fileInfo.Progress:F1}%)",
            FileCopyStatus.CopyComplete => $"📄 {fileName} - ✅ Downloaded",
            FileCopyStatus.Verifying => $"📄 {fileName} - 🔍 Verifying...",
            FileCopyStatus.Verified => $"📄 {fileName} - ✅ Verified",
            FileCopyStatus.HashMismatch => $"📄 {fileName} - ⚠️ Hash mismatch",
            FileCopyStatus.Failed => $"📄 {fileName} - ❌ Failed: {fileInfo.ErrorMessage}",
            _ => $"📄 {fileName} - ⏸️ Pending"
        };

        node.Text = status;
        node.ForeColor = fileInfo.Status switch
        {
            FileCopyStatus.Verified => Color.Green,
            FileCopyStatus.HashMismatch => Color.Orange,
            FileCopyStatus.Failed => Color.Red,
            FileCopyStatus.Copying or FileCopyStatus.Verifying => Color.Blue,
            _ => Color.Black
        };

        if (fileInfo.Status == FileCopyStatus.Verified || fileInfo.Status == FileCopyStatus.HashMismatch)
        {
            // Add hash info as child nodes
            node.Nodes.Clear();
            node.Nodes.Add(new TreeNode($"Expected: {fileInfo.ExpectedHash ?? "N/A"}") 
            { 
                ForeColor = Color.Gray 
            });
            node.Nodes.Add(new TreeNode($"Actual:   {fileInfo.ActualHash ?? "N/A"}") 
            { 
                ForeColor = fileInfo.IsHashMatch ? Color.Green : Color.Red 
            });
        }

        _progressBar.Value = Math.Min(_progressBar.Value + 1, _progressBar.Maximum);
        _treeView.Refresh();
    }

    #endregion

    #region Step 2: Verify Checksums

    private async void BtnStep2_Click(object? sender, EventArgs e)
    {
        _btnStep2.Enabled = false;

        var step2Node = FindStepNode(2);
        if (step2Node == null) return;

        step2Node.Text = "✅ Step 2: Verify Checksums (Already completed in Step 1)";
        step2Node.ForeColor = Color.Green;
        _lblStatus.Text = "✅ Step 2: Checksums already verified during download.";

        // Enable Step 3 if destinations configured
        if (_destinations.Any(d => d.IsEnabled))
        {
            _btnStep3.Enabled = true;
            _btnStep3.BackColor = Color.FromArgb(52, 152, 219);
            _btnStep3.Cursor = Cursors.Hand;
        }
        else
        {
            MessageBox.Show(
                "No destinations configured.\n\n" +
                "Please configure destinations before proceeding to Step 3.",
                "No Destinations",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        await Task.CompletedTask;
    }

    #endregion

    #region Step 3: Copy to Multiple Destinations

    private async void BtnStep3_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_copiedPackagePath) || !Directory.Exists(_copiedPackagePath))
        {
            MessageBox.Show("Local package not found. Please complete Step 1 first.",
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var enabledDests = _destinations.Where(d => d.IsEnabled).ToList();
        if (enabledDests.Count == 0)
        {
            MessageBox.Show("No destinations enabled.", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnStep3.Enabled = false;
        _cts = new CancellationTokenSource();

        try
        {
            var step3Node = FindStepNode(3);
            if (step3Node == null) return;

            step3Node.Text = $"⏳ Step 3: Copy to {enabledDests.Count} Destination(s) (In progress...)";
            step3Node.ForeColor = Color.Blue;
            step3Node.Nodes.Clear();

            // Create destination nodes
            foreach (var dest in enabledDests)
            {
                var destNode = new TreeNode($"📍 {dest.Name} ({dest.UncPath})")
                {
                    Tag = new NodeData { Type = NodeType.Destination, DestinationName = dest.Name },
                    ForeColor = Color.Gray
                };
                step3Node.Nodes.Add(destNode);
            }
            step3Node.Expand();

            _lblStatus.Text = $"🔄 Copying to {enabledDests.Count} destination(s)...";
            _progressBar.Value = 0;
            _progressBar.Maximum = enabledDests.Count * _package.FileCount;

            var progress = new Progress<List<DestinationCopyResult>>(results =>
            {
                if (InvokeRequired)
                {
                    Invoke(() => UpdateMultiCopyProgress(step3Node, results));
                }
                else
                {
                    UpdateMultiCopyProgress(step3Node, results);
                }
            });

            _multiCopyResults = await _multiCopyService.CopyToMultipleDestinationsAsync(
                _copiedPackagePath,
                _package.FolderName,
                enabledDests,
                progress,
                _cts.Token);

            var successCount = _multiCopyResults.Count(r => r.Status == DestinationStatus.Completed);
            var errorCount = _multiCopyResults.Count(r => r.Status == DestinationStatus.Failed || 
                                                           r.Status == DestinationStatus.CompletedWithErrors);

            if (errorCount == 0)
            {
                step3Node.Text = $"✅ Step 3: Copy to Destinations (Complete - {successCount}/{enabledDests.Count} successful)";
                step3Node.ForeColor = Color.Green;
                _lblStatus.Text = $"✅ All steps complete! {successCount}/{enabledDests.Count} destinations successful.";
            }
            else
            {
                step3Node.Text = $"⚠️ Step 3: Copy to Destinations (Completed with errors - {errorCount} failed)";
                step3Node.ForeColor = Color.Orange;
                _lblStatus.Text = $"⚠️ Step 3 completed with {errorCount} destination errors.";
            }
        }
        catch (Exception ex)
        {
            var step3Node = FindStepNode(3);
            if (step3Node != null)
            {
                step3Node.Text = "❌ Step 3: Copy to Destinations (Failed)";
                step3Node.ForeColor = Color.Red;
            }

            _lblStatus.Text = $"❌ Error: {ex.Message}";
            MessageBox.Show($"Error during multi-destination copy:\n\n{ex.Message}",
                "Step 3 Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void UpdateMultiCopyProgress(TreeNode step3Node, List<DestinationCopyResult> results)
    {
        // Suspend layout updates to prevent flickering
        _treeView.BeginUpdate();

        try
        {
            foreach (var result in results)
            {
                var destNode = step3Node.Nodes.Cast<TreeNode>()
                    .FirstOrDefault(n => n.Tag is NodeData data && data.DestinationName == result.DestinationName);

                if (destNode == null) continue;

                // Update destination node
                var statusIcon = result.Status switch
                {
                    DestinationStatus.Pending => "⏸️",
                    DestinationStatus.Connecting => "🔗",
                    DestinationStatus.Copying => "⏳",
                    DestinationStatus.Verifying => "🔍",
                    DestinationStatus.Completed => "✅",
                    DestinationStatus.CompletedWithErrors => "⚠️",
                    DestinationStatus.Failed => "❌",
                    _ => "⏸️"
                };

                var statusText = result.Status == DestinationStatus.Failed && !string.IsNullOrEmpty(result.ErrorMessage)
                    ? $"{statusIcon} {result.DestinationName} - Failed: {result.ErrorMessage}"
                    : $"{statusIcon} {result.DestinationName} - Copied: {result.FilesCopied}/{result.FilesTotal}, Verified: {result.FilesVerified}/{result.FilesTotal}";

                destNode.Text = statusText;

                destNode.ForeColor = result.Status switch
                {
                    DestinationStatus.Completed => Color.Green,
                    DestinationStatus.CompletedWithErrors => Color.Orange,
                    DestinationStatus.Failed => Color.Red,
                    DestinationStatus.Copying or DestinationStatus.Verifying => Color.Blue,
                    _ => Color.Gray
                };

                // Update file nodes under destination only if files list changed
                if (result.Files.Any())
                {
                    destNode.Nodes.Clear();
                    foreach (var fileInfo in result.Files)
                    {
                        var fileStatus = fileInfo.Status switch
                        {
                            FileCopyStatus.Pending => "⏸️",
                            FileCopyStatus.Copying => $"⏳ {fileInfo.Progress:F1}%",
                            FileCopyStatus.CopyComplete => "✅ Copied",
                            FileCopyStatus.Verifying => "🔍 Verifying",
                            FileCopyStatus.Verified => "✅ Verified",
                            FileCopyStatus.HashMismatch => "⚠️ Hash mismatch",
                            FileCopyStatus.Failed => $"❌ {fileInfo.ErrorMessage}",
                            _ => "⏸️"
                        };

                        var fileNode = new TreeNode($"📄 {fileInfo.FileName} - {fileStatus}")
                        {
                            ForeColor = fileInfo.Status switch
                            {
                                FileCopyStatus.Verified => Color.Green,
                                FileCopyStatus.HashMismatch => Color.Orange,
                                FileCopyStatus.Failed => Color.Red,
                                _ => Color.Gray
                            }
                        };

                        // Add hash comparison for verified/mismatched files
                        if (fileInfo.Status == FileCopyStatus.Verified || fileInfo.Status == FileCopyStatus.HashMismatch)
                        {
                            fileNode.Nodes.Add(new TreeNode($"Expected: {fileInfo.ExpectedHash ?? "N/A"}") 
                            { 
                                ForeColor = Color.Gray 
                            });
                            fileNode.Nodes.Add(new TreeNode($"Actual:   {fileInfo.ActualHash ?? "N/A"}") 
                            { 
                                ForeColor = fileInfo.IsHashMatch ? Color.Green : Color.Red 
                            });
                        }

                        destNode.Nodes.Add(fileNode);
                    }
                }

                if (result.Status != DestinationStatus.Pending)
                {
                    destNode.Expand();
                }
            }
        }
        finally
        {
            // Resume layout updates
            _treeView.EndUpdate();
        }
    }

    #endregion

    #region Helper Methods

    private TreeNode? FindStepNode(int stepNumber)
    {
        if (_treeView.Nodes.Count == 0) return null;
        var rootNode = _treeView.Nodes[0];

        foreach (TreeNode node in rootNode.Nodes)
        {
            if (node.Tag is NodeData data && data.Type == NodeType.Step && data.StepNumber == stepNumber)
                return node;
        }

        return null;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnFormClosing(e);
    }

    #endregion

    #region Supporting Classes

    private class NodeData
    {
        public NodeType Type { get; set; }
        public int StepNumber { get; set; }
        public string? DestinationName { get; set; }
        public FileCopyInfo? FileInfo { get; set; }
    }

    private enum NodeType
    {
        Package,
        Step,
        Destination,
        File
    }

    #endregion
}
