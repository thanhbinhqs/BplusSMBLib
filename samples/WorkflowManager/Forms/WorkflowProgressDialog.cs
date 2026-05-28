using WorkflowManager.Models;

namespace WorkflowManager.Forms;

/// <summary>
/// Dialog hiển thị toàn bộ workflow với tree structure
/// </summary>
public class WorkflowProgressDialog : Form
{
    private TreeView _treeView = null!;
    private Label _lblOverallStatus = null!;
    private ProgressBar _overallProgress = null!;
    private Button _btnClose = null!;
    private ImageList _imageList = null!;

    // Tree nodes for workflow steps
    private TreeNode? _downloadNode;
    private TreeNode? _verifyNode;
    private TreeNode? _multiCopyNode;

    private Dictionary<string, TreeNode> _downloadFileNodes = new();
    private Dictionary<string, TreeNode> _verifyFileNodes = new();
    private Dictionary<string, Dictionary<string, TreeNode>> _destinationNodes = new();

    private bool _isComplete = false;
    private string _packageName = "";

    public WorkflowProgressDialog(string packageName)
    {
        _packageName = packageName;
        InitializeUI();
    }

    private void InitializeUI()
    {
        Text = $"Workflow Progress: {_packageName}";
        Size = new Size(1400, 800);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(1200, 600);
        BackColor = Color.FromArgb(250, 250, 252);

        // Create image list for tree icons
        _imageList = new ImageList();
        _imageList.ColorDepth = ColorDepth.Depth32Bit;
        _imageList.ImageSize = new Size(16, 16);

        // Add status icons (using Unicode characters as fallback)
        var icons = new Dictionary<string, Color>
        {
            ["pending"] = Color.Gray,
            ["running"] = Color.Blue,
            ["success"] = Color.Green,
            ["warning"] = Color.Orange,
            ["error"] = Color.Red,
            ["verifying"] = Color.Purple
        };

        foreach (var (name, color) in icons)
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillEllipse(new SolidBrush(color), 2, 2, 12, 12);
            }
            _imageList.Images.Add(name, bmp);
        }

        // Title
        var lblTitle = new Label
        {
            Text = $"📦 Package Workflow: {_packageName}",
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            Location = new Point(15, 15),
            AutoSize = true
        };
        Controls.Add(lblTitle);

        // Overall status
        _lblOverallStatus = new Label
        {
            Text = "Starting workflow...",
            Location = new Point(15, 50),
            AutoSize = true,
            Font = new Font("Segoe UI", 9F),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        Controls.Add(_lblOverallStatus);

        // Overall progress
        _overallProgress = new ProgressBar
        {
            Location = new Point(15, 75),
            Size = new Size(ClientSize.Width - 30, 25),
            Style = ProgressBarStyle.Continuous,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        Controls.Add(_overallProgress);

        // TreeView
        _treeView = new TreeView
        {
            Location = new Point(15, 110),
            Size = new Size(ClientSize.Width - 30, ClientSize.Height - 180),
            Font = new Font("Consolas", 9F),
            BorderStyle = BorderStyle.FixedSingle,
            ImageList = _imageList,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ShowLines = true,
            ShowPlusMinus = true,
            ShowRootLines = true,
            FullRowSelect = true,
            HideSelection = false
        };
        Controls.Add(_treeView);

        // Close button
        _btnClose = new Button
        {
            Text = "⏳ Working...",
            Size = new Size(150, 35),
            Location = new Point(ClientSize.Width - 165, ClientSize.Height - 50),
            Enabled = false,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            BackColor = Color.Gray,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Default,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _btnClose.FlatAppearance.BorderSize = 0;
        _btnClose.Click += (s, e) => Close();

        var tooltip = new ToolTip();
        tooltip.SetToolTip(_btnClose, "Please wait until workflow completes...");

        Controls.Add(_btnClose);

        InitializeWorkflowTree();
    }

    private void InitializeWorkflowTree()
    {
        _treeView.Nodes.Clear();

        // Step 1: Download from SMB
        _downloadNode = new TreeNode("Step 1: Download from SMB")
        {
            ImageKey = "pending",
            SelectedImageKey = "pending",
            Tag = new StepInfo { Status = StepStatus.Pending }
        };
        _treeView.Nodes.Add(_downloadNode);

        // Step 2: Verify Checksums
        _verifyNode = new TreeNode("Step 2: Verify Checksums")
        {
            ImageKey = "pending",
            SelectedImageKey = "pending",
            Tag = new StepInfo { Status = StepStatus.Pending }
        };
        _treeView.Nodes.Add(_verifyNode);

        // Step 3: Copy to Multiple Destinations
        _multiCopyNode = new TreeNode("Step 3: Copy to Multiple Destinations")
        {
            ImageKey = "pending",
            SelectedImageKey = "pending",
            Tag = new StepInfo { Status = StepStatus.Pending }
        };
        _treeView.Nodes.Add(_multiCopyNode);

        _treeView.ExpandAll();
    }

    #region Step 1: Download from SMB

    public void StartDownload(int totalFiles)
    {
        if (InvokeRequired)
        {
            Invoke(() => StartDownload(totalFiles));
            return;
        }

        UpdateStepNode(_downloadNode, StepStatus.Running, $"Downloading {totalFiles} files from SMB...");
        _lblOverallStatus.Text = "Step 1/3: Downloading from SMB...";
    }

    public void AddDownloadFile(FileCopyInfo fileInfo)
    {
        if (InvokeRequired)
        {
            Invoke(() => AddDownloadFile(fileInfo));
            return;
        }

        if (_downloadNode == null || _downloadFileNodes.ContainsKey(fileInfo.FileName))
            return;

        var fileNode = new TreeNode($"{fileInfo.FileName} - {FormatFileSize(fileInfo.FileSize)}")
        {
            ImageKey = "pending",
            SelectedImageKey = "pending",
            Tag = new FileStepInfo 
            { 
                FileName = fileInfo.FileName,
                Status = StepStatus.Pending 
            }
        };

        _downloadNode.Nodes.Add(fileNode);
        _downloadFileNodes[fileInfo.FileName] = fileNode;
        _downloadNode.Expand();
    }

    public void UpdateDownloadProgress(FileCopyInfo fileInfo)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateDownloadProgress(fileInfo));
            return;
        }

        if (!_downloadFileNodes.TryGetValue(fileInfo.FileName, out var fileNode))
            return;

        var status = fileInfo.Status switch
        {
            FileCopyStatus.Copying => StepStatus.Running,
            FileCopyStatus.CopyComplete => StepStatus.Success,
            FileCopyStatus.Failed => StepStatus.Error,
            _ => StepStatus.Pending
        };

        var icon = status switch
        {
            StepStatus.Running => "running",
            StepStatus.Success => "success",
            StepStatus.Error => "error",
            _ => "pending"
        };

        var statusText = fileInfo.Status switch
        {
            FileCopyStatus.Copying => $"Copying... {fileInfo.Progress:F1}%",
            FileCopyStatus.CopyComplete => "Downloaded ✓",
            FileCopyStatus.Failed => $"Failed: {fileInfo.ErrorMessage}",
            _ => "Pending"
        };

        fileNode.Text = $"{fileInfo.FileName} - {FormatFileSize(fileInfo.FileSize)} - {statusText}";
        fileNode.ImageKey = icon;
        fileNode.SelectedImageKey = icon;

        if (fileNode.Tag is FileStepInfo info)
        {
            info.Status = status;
        }

        UpdateStepProgress(_downloadNode);
    }

    public void CompleteDownload(bool success, string message)
    {
        if (InvokeRequired)
        {
            Invoke(() => CompleteDownload(success, message));
            return;
        }

        UpdateStepNode(_downloadNode, success ? StepStatus.Success : StepStatus.Error, message);
    }

    #endregion

    #region Step 2: Verify Checksums

    public void StartVerification(int totalFiles)
    {
        if (InvokeRequired)
        {
            Invoke(() => StartVerification(totalFiles));
            return;
        }

        UpdateStepNode(_verifyNode, StepStatus.Running, $"Verifying {totalFiles} files...");
        _lblOverallStatus.Text = "Step 2/3: Verifying checksums...";
    }

    public void AddVerifyFile(FileCopyInfo fileInfo)
    {
        if (InvokeRequired)
        {
            Invoke(() => AddVerifyFile(fileInfo));
            return;
        }

        if (_verifyNode == null || _verifyFileNodes.ContainsKey(fileInfo.FileName))
            return;

        var fileNode = new TreeNode($"{fileInfo.FileName}")
        {
            ImageKey = "pending",
            SelectedImageKey = "pending",
            Tag = new FileStepInfo 
            { 
                FileName = fileInfo.FileName,
                Status = StepStatus.Pending 
            }
        };

        // Add expected hash as child node
        if (!string.IsNullOrEmpty(fileInfo.ExpectedHash))
        {
            var expectedNode = new TreeNode($"Expected: {fileInfo.ExpectedHash}")
            {
                ImageKey = "pending",
                SelectedImageKey = "pending",
                ForeColor = Color.Gray
            };
            fileNode.Nodes.Add(expectedNode);
        }

        _verifyNode.Nodes.Add(fileNode);
        _verifyFileNodes[fileInfo.FileName] = fileNode;
        _verifyNode.Expand();
    }

    public void UpdateVerifyProgress(FileCopyInfo fileInfo)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateVerifyProgress(fileInfo));
            return;
        }

        if (!_verifyFileNodes.TryGetValue(fileInfo.FileName, out var fileNode))
            return;

        var status = fileInfo.Status switch
        {
            FileCopyStatus.Verifying => StepStatus.Running,
            FileCopyStatus.Verified => StepStatus.Success,
            FileCopyStatus.HashMismatch => StepStatus.Warning,
            FileCopyStatus.Failed => StepStatus.Error,
            _ => StepStatus.Pending
        };

        var icon = status switch
        {
            StepStatus.Running => "verifying",
            StepStatus.Success => "success",
            StepStatus.Warning => "warning",
            StepStatus.Error => "error",
            _ => "pending"
        };

        var statusText = fileInfo.Status switch
        {
            FileCopyStatus.Verifying => "Computing MD5...",
            FileCopyStatus.Verified => "Verified ✓",
            FileCopyStatus.HashMismatch => "Hash Mismatch ⚠",
            FileCopyStatus.Failed => "Failed ✗",
            _ => "Pending"
        };

        fileNode.Text = $"{fileInfo.FileName} - {statusText}";
        fileNode.ImageKey = icon;
        fileNode.SelectedImageKey = icon;

        // Update actual hash node
        if (!string.IsNullOrEmpty(fileInfo.ActualHash))
        {
            // Remove old actual hash node if exists
            var actualNode = fileNode.Nodes.Cast<TreeNode>()
                .FirstOrDefault(n => n.Text.StartsWith("Actual:"));

            if (actualNode != null)
            {
                fileNode.Nodes.Remove(actualNode);
            }

            // Add new actual hash node
            var hashMatch = fileInfo.IsHashMatch;
            actualNode = new TreeNode($"Actual:   {fileInfo.ActualHash}")
            {
                ImageKey = hashMatch ? "success" : "warning",
                SelectedImageKey = hashMatch ? "success" : "warning",
                ForeColor = hashMatch ? Color.Green : Color.Red
            };
            fileNode.Nodes.Add(actualNode);
            fileNode.Expand();
        }

        if (fileNode.Tag is FileStepInfo info)
        {
            info.Status = status;
        }

        UpdateStepProgress(_verifyNode);
    }

    public void CompleteVerification(bool success, string message)
    {
        if (InvokeRequired)
        {
            Invoke(() => CompleteVerification(success, message));
            return;
        }

        UpdateStepNode(_verifyNode, success ? StepStatus.Success : StepStatus.Warning, message);
    }

    #endregion

    #region Step 3: Multi-Destination Copy

    public void StartMultiCopy(List<DestinationInfo> destinations)
    {
        if (InvokeRequired)
        {
            Invoke(() => StartMultiCopy(destinations));
            return;
        }

        UpdateStepNode(_multiCopyNode, StepStatus.Running, $"Copying to {destinations.Count} destination(s)...");
        _lblOverallStatus.Text = "Step 3/3: Copying to multiple destinations...";

        // Create destination nodes
        foreach (var dest in destinations.Where(d => d.IsEnabled))
        {
            var destNode = new TreeNode($"📍 {dest.Name} - {dest.UncPath}")
            {
                ImageKey = "pending",
                SelectedImageKey = "pending",
                Tag = new DestinationStepInfo 
                { 
                    DestinationName = dest.Name,
                    Status = StepStatus.Pending 
                }
            };

            _multiCopyNode!.Nodes.Add(destNode);

            var fileNodes = new Dictionary<string, TreeNode>();
            _destinationNodes[dest.Name] = fileNodes;
        }

        _multiCopyNode!.Expand();
    }

    public void UpdateMultiCopyProgress(List<DestinationCopyResult> results)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateMultiCopyProgress(results));
            return;
        }

        foreach (var result in results)
        {
            UpdateDestinationProgress(result);
        }

        // Update overall multi-copy status
        var allCompleted = results.All(r => 
            r.Status == DestinationStatus.Completed || 
            r.Status == DestinationStatus.CompletedWithErrors ||
            r.Status == DestinationStatus.Failed);

        if (allCompleted)
        {
            var allSuccess = results.All(r => r.Status == DestinationStatus.Completed);
            var anyErrors = results.Any(r => 
                r.Status == DestinationStatus.CompletedWithErrors || 
                r.Status == DestinationStatus.Failed);

            var status = allSuccess ? StepStatus.Success : 
                        anyErrors ? StepStatus.Warning : StepStatus.Error;

            var successCount = results.Count(r => r.Status == DestinationStatus.Completed);
            var message = $"Completed: {successCount}/{results.Count} successful";

            UpdateStepNode(_multiCopyNode, status, message);

            // Mark workflow as complete
            if (!_isComplete)
            {
                _isComplete = true;
                _btnClose.Enabled = true;
                _lblOverallStatus.Text = allSuccess 
                    ? "✅ Workflow completed successfully!" 
                    : "⚠️ Workflow completed with some errors";
                _overallProgress.Value = _overallProgress.Maximum;
            }
        }
    }

    private void UpdateDestinationProgress(DestinationCopyResult result)
    {
        var destNode = _multiCopyNode?.Nodes.Cast<TreeNode>()
            .FirstOrDefault(n => n.Tag is DestinationStepInfo info && info.DestinationName == result.DestinationName);

        if (destNode == null)
            return;

        // Update destination node
        var status = result.Status switch
        {
            DestinationStatus.Connecting => StepStatus.Running,
            DestinationStatus.Copying => StepStatus.Running,
            DestinationStatus.Verifying => StepStatus.Running,
            DestinationStatus.Completed => StepStatus.Success,
            DestinationStatus.CompletedWithErrors => StepStatus.Warning,
            DestinationStatus.Failed => StepStatus.Error,
            _ => StepStatus.Pending
        };

        var icon = status switch
        {
            StepStatus.Running => "running",
            StepStatus.Success => "success",
            StepStatus.Warning => "warning",
            StepStatus.Error => "error",
            _ => "pending"
        };

        var statusText = result.Status switch
        {
            DestinationStatus.Connecting => "Connecting...",
            DestinationStatus.Copying => $"Copying... {result.FilesCopied}/{result.FilesTotal}",
            DestinationStatus.Verifying => $"Verifying... {result.FilesVerified}/{result.FilesTotal}",
            DestinationStatus.Completed => $"Completed ✓ ({result.FilesVerified} verified)",
            DestinationStatus.CompletedWithErrors => $"Completed with errors ({result.FilesFailed} failed)",
            DestinationStatus.Failed => $"Failed: {result.ErrorMessage}",
            _ => "Pending"
        };

        destNode.Text = $"📍 {result.DestinationName} - {statusText}";
        destNode.ImageKey = icon;
        destNode.SelectedImageKey = icon;

        if (destNode.Tag is DestinationStepInfo info)
        {
            info.Status = status;
        }

        // Update file nodes for this destination
        if (_destinationNodes.TryGetValue(result.DestinationName, out var fileNodes))
        {
            foreach (var fileInfo in result.Files)
            {
                if (!fileNodes.TryGetValue(fileInfo.FileName, out var fileNode))
                {
                    fileNode = new TreeNode(fileInfo.FileName)
                    {
                        ImageKey = "pending",
                        SelectedImageKey = "pending"
                    };
                    destNode.Nodes.Add(fileNode);
                    fileNodes[fileInfo.FileName] = fileNode;
                }

                var fileStatus = fileInfo.Status switch
                {
                    FileCopyStatus.Copying => "running",
                    FileCopyStatus.CopyComplete => "success",
                    FileCopyStatus.Verifying => "verifying",
                    FileCopyStatus.Verified => "success",
                    FileCopyStatus.HashMismatch => "warning",
                    FileCopyStatus.Failed => "error",
                    _ => "pending"
                };

                var fileStatusText = fileInfo.Status switch
                {
                    FileCopyStatus.Copying => $"Copying... {fileInfo.Progress:F1}%",
                    FileCopyStatus.CopyComplete => "Copied ✓",
                    FileCopyStatus.Verifying => "Verifying...",
                    FileCopyStatus.Verified => "Verified ✓",
                    FileCopyStatus.HashMismatch => "Hash Mismatch ⚠",
                    FileCopyStatus.Failed => $"Failed: {fileInfo.ErrorMessage}",
                    _ => "Pending"
                };

                fileNode.Text = $"{fileInfo.FileName} - {fileStatusText}";
                fileNode.ImageKey = fileStatus;
                fileNode.SelectedImageKey = fileStatus;
            }

            if (destNode.Nodes.Count > 0)
            {
                destNode.Expand();
            }
        }
    }

    #endregion

    #region Helper Methods

    private void UpdateStepNode(TreeNode? node, StepStatus status, string message)
    {
        if (node == null)
            return;

        var icon = status switch
        {
            StepStatus.Running => "running",
            StepStatus.Success => "success",
            StepStatus.Warning => "warning",
            StepStatus.Error => "error",
            _ => "pending"
        };

        var originalText = node.Text.Split('-')[0].Trim();
        node.Text = $"{originalText} - {message}";
        node.ImageKey = icon;
        node.SelectedImageKey = icon;

        if (node.Tag is StepInfo info)
        {
            info.Status = status;
        }

        UpdateOverallProgress();
    }

    private void UpdateStepProgress(TreeNode? parentNode)
    {
        if (parentNode == null || parentNode.Nodes.Count == 0)
            return;

        var total = parentNode.Nodes.Count;
        var completed = parentNode.Nodes.Cast<TreeNode>()
            .Count(n => n.Tag is FileStepInfo info && 
                       (info.Status == StepStatus.Success || 
                        info.Status == StepStatus.Warning || 
                        info.Status == StepStatus.Error));

        var success = parentNode.Nodes.Cast<TreeNode>()
            .Count(n => n.Tag is FileStepInfo info && info.Status == StepStatus.Success);

        var failed = parentNode.Nodes.Cast<TreeNode>()
            .Count(n => n.Tag is FileStepInfo info && info.Status == StepStatus.Error);

        if (completed == total)
        {
            var status = failed > 0 ? StepStatus.Warning : StepStatus.Success;
            var message = $"Completed: {success}/{total} successful";
            if (failed > 0)
            {
                message += $", {failed} failed";
            }

            UpdateStepNode(parentNode, status, message);
        }
    }

    private void UpdateOverallProgress()
    {
        var totalSteps = 3;
        var completedSteps = 0;

        if (_downloadNode?.Tag is StepInfo downloadInfo && 
            (downloadInfo.Status == StepStatus.Success || downloadInfo.Status == StepStatus.Error))
            completedSteps++;

        if (_verifyNode?.Tag is StepInfo verifyInfo && 
            (verifyInfo.Status == StepStatus.Success || verifyInfo.Status == StepStatus.Warning))
            completedSteps++;

        if (_multiCopyNode?.Tag is StepInfo multiCopyInfo && 
            (multiCopyInfo.Status == StepStatus.Success || 
             multiCopyInfo.Status == StepStatus.Warning || 
             multiCopyInfo.Status == StepStatus.Error))
            completedSteps++;

        _overallProgress.Maximum = totalSteps;
        _overallProgress.Value = completedSteps;

        // Update overall status text
        if (completedSteps == totalSteps)
        {
            _lblOverallStatus.Text = "✅ All steps completed! You can now close this dialog.";
            _lblOverallStatus.ForeColor = Color.Green;

            // Enable close button
            EnableCloseButton();
        }
        else
        {
            _lblOverallStatus.Text = $"⏳ Processing... Step {completedSteps + 1} of {totalSteps}";
            _lblOverallStatus.ForeColor = Color.Blue;
        }
    }

    private void EnableCloseButton()
    {
        if (InvokeRequired)
        {
            Invoke(EnableCloseButton);
            return;
        }

        _btnClose.Text = "✅ Close";
        _btnClose.Enabled = true;
        _btnClose.BackColor = Color.FromArgb(52, 152, 219);
        _btnClose.Cursor = Cursors.Hand;

        var tooltip = new ToolTip();
        tooltip.SetToolTip(_btnClose, "Close this window (Workflow is complete)");
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    #endregion

    #region Supporting Classes

    private class StepInfo
    {
        public StepStatus Status { get; set; }
    }

    private class FileStepInfo
    {
        public string FileName { get; set; } = "";
        public StepStatus Status { get; set; }
    }

    private class DestinationStepInfo
    {
        public string DestinationName { get; set; } = "";
        public StepStatus Status { get; set; }
    }

    private enum StepStatus
    {
        Pending,
        Running,
        Success,
        Warning,
        Error
    }

    #endregion
}
