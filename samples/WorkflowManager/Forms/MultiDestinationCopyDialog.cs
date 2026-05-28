using WorkflowManager.Models;

namespace WorkflowManager.Forms;

/// <summary>
/// Dialog hiển thị progress khi copy đến nhiều destinations
/// </summary>
public class MultiDestinationCopyDialog : Form
{
    private Panel _mainPanel = null!;
    private Label _lblOverallStatus = null!;
    private ProgressBar _overallProgress = null!;
    private Button _btnClose = null!;
    private Dictionary<string, DestinationPanel> _destinationPanels = new();
    private bool _isComplete = false;
    private int _destinationCount;

    public MultiDestinationCopyDialog(string packageName, List<DestinationInfo> destinations)
    {
        _destinationCount = destinations.Count(d => d.IsEnabled);
        InitializeUI(packageName, destinations);
    }

    private void InitializeUI(string packageName, List<DestinationInfo> destinations)
    {
        Text = $"Multi-Destination Copy: {packageName}";
        Size = new Size(1400, 800);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(1200, 600);
        BackColor = Color.FromArgb(245, 247, 250);

        // Header Panel
        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 120,
            BackColor = Color.White,
            Padding = new Padding(20)
        };

        // Add subtle border
        headerPanel.Paint += (s, e) =>
        {
            var rect = headerPanel.ClientRectangle;
            using (var pen = new Pen(Color.FromArgb(200, 200, 210), 1))
            {
                e.Graphics.DrawLine(pen, 0, rect.Height - 1, rect.Width, rect.Height - 1);
            }
        };

        // Title
        var lblTitle = new Label
        {
            Text = $"📤 Copying to {_destinationCount} Destination(s)",
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            Location = new Point(0, 5),
            AutoSize = true,
            ForeColor = Color.FromArgb(44, 62, 80)
        };
        headerPanel.Controls.Add(lblTitle);

        // Overall status
        _lblOverallStatus = new Label
        {
            Text = $"Destinations: 0/{_destinationCount} | Files: 0 verified, 0 failed, 0/0 total",
            Location = new Point(0, 40),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = Color.FromArgb(90, 100, 110)
        };
        headerPanel.Controls.Add(_lblOverallStatus);

        // Overall progress
        _overallProgress = new ProgressBar
        {
            Location = new Point(0, 68),
            Size = new Size(headerPanel.Width - 40, 30),
            Style = ProgressBarStyle.Continuous,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        headerPanel.Controls.Add(_overallProgress);

        Controls.Add(headerPanel);

        // Scrollable main panel for destinations
        var scrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.FromArgb(245, 247, 250),
            Padding = new Padding(20)
        };

        _mainPanel = new Panel
        {
            Location = new Point(0, 0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = scrollPanel.Width - 40
        };

        scrollPanel.Controls.Add(_mainPanel);
        Controls.Add(scrollPanel);

        // Create panels for each destination
        int yOffset = 10;
        foreach (var dest in destinations.Where(d => d.IsEnabled))
        {
            var panel = new DestinationPanel(dest.Name, _mainPanel.Width - 20);
            panel.Panel.Location = new Point(0, yOffset);
            _mainPanel.Controls.Add(panel.Panel);
            _destinationPanels[dest.Name] = panel;
            yOffset += panel.Panel.Height + 15;
        }

        // Bottom panel with close button
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 70,
            BackColor = Color.White,
            Padding = new Padding(20)
        };

        bottomPanel.Paint += (s, e) =>
        {
            var rect = bottomPanel.ClientRectangle;
            using (var pen = new Pen(Color.FromArgb(200, 200, 210), 1))
            {
                e.Graphics.DrawLine(pen, 0, 0, rect.Width, 0);
            }
        };

        // Close button
        _btnClose = new Button
        {
            Text = "Close",
            Size = new Size(120, 38),
            Location = new Point(bottomPanel.Width - 140, 16),
            Enabled = false,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            BackColor = Color.FromArgb(52, 152, 219),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _btnClose.FlatAppearance.BorderSize = 0;
        _btnClose.Click += (s, e) => Close();
        bottomPanel.Controls.Add(_btnClose);

        Controls.Add(bottomPanel);
    }

    public void UpdateProgress(List<DestinationCopyResult> results)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateProgress(results));
            return;
        }

        // Update overall progress
        var totalFiles = results.Sum(r => r.FilesTotal);
        var completedFiles = results.Sum(r => r.FilesCopied + r.FilesVerified + r.FilesFailed);
        var successFiles = results.Sum(r => r.FilesVerified);
        var failedFiles = results.Sum(r => r.FilesFailed);

        if (totalFiles > 0)
        {
            _overallProgress.Maximum = totalFiles * _destinationCount;
            _overallProgress.Value = Math.Min(completedFiles, totalFiles * _destinationCount);
        }

        var completedDests = results.Count(r => 
            r.Status == DestinationStatus.Completed || 
            r.Status == DestinationStatus.CompletedWithErrors ||
            r.Status == DestinationStatus.Failed);

        _lblOverallStatus.Text = $"Destinations: {completedDests}/{_destinationCount} | " +
                                 $"Files: {successFiles} verified, {failedFiles} failed, {completedFiles}/{totalFiles * _destinationCount} total";

        // Update each destination panel
        foreach (var result in results)
        {
            if (_destinationPanels.TryGetValue(result.DestinationName, out var panel))
            {
                panel.UpdateProgress(result);
            }
        }

        // Check if all complete
        if (completedDests == _destinationCount && !_isComplete)
        {
            _isComplete = true;
            _btnClose.Enabled = true;

            var allSuccess = results.All(r => r.Status == DestinationStatus.Completed);
            if (allSuccess)
            {
                _lblOverallStatus.Text = "✅ All destinations completed successfully!";
                _lblOverallStatus.ForeColor = Color.FromArgb(46, 204, 113);
            }
            else
            {
                _lblOverallStatus.Text = $"⚠️ Completed with {failedFiles} file errors across {results.Count(r => r.Status != DestinationStatus.Completed)} destination(s)";
                _lblOverallStatus.ForeColor = Color.FromArgb(230, 126, 34);
            }
        }
    }

    private class DestinationPanel
    {
        public Panel Panel { get; }
        private Label _lblDestName = null!;
        private Label _lblStatus = null!;
        private Label _lblStats = null!;
        private ProgressBar _progressBar = null!;
        private ListView _lvFiles = null!;
        private Dictionary<string, ListViewItem> _fileItems = new();

        public DestinationPanel(string destinationName, int width)
        {
            Panel = new Panel
            {
                Width = width,
                Height = 280,
                BackColor = Color.White,
                Padding = new Padding(15)
            };

            // Add border
            Panel.Paint += (s, e) =>
            {
                var rect = Panel.ClientRectangle;
                using (var pen = new Pen(Color.FromArgb(220, 220, 230), 2))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, rect.Width - 1, rect.Height - 1);
                }
            };

            InitializeControls(destinationName);
        }

        private void InitializeControls(string destinationName)
        {
            // Destination name header
            _lblDestName = new Label
            {
                Text = destinationName,
                Location = new Point(15, 12),
                AutoSize = true,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 73, 94)
            };
            Panel.Controls.Add(_lblDestName);

            // Status label
            _lblStatus = new Label
            {
                Text = "⏳ Pending",
                Location = new Point(15, 42),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.Gray
            };
            Panel.Controls.Add(_lblStatus);

            // Stats label
            _lblStats = new Label
            {
                Text = "0 verified, 8 failed, 0/8 copied - Duration: 00:00",
                Location = new Point(15, 66),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(100, 110, 120)
            };
            Panel.Controls.Add(_lblStats);

            // Progress bar
            _progressBar = new ProgressBar
            {
                Location = new Point(15, 92),
                Size = new Size(Panel.Width - 30, 25),
                Style = ProgressBarStyle.Continuous,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Panel.Controls.Add(_progressBar);

            // Files list (compact view)
            _lvFiles = new ListView
            {
                Location = new Point(15, 125),
                Size = new Size(Panel.Width - 30, Panel.Height - 140),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Consolas", 8.5F),
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                ShowItemToolTips = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            _lvFiles.Columns.Add("File Name", 220);
            _lvFiles.Columns.Add("Size", 80);
            _lvFiles.Columns.Add("Progress", 80);
            _lvFiles.Columns.Add("Status", 120);
            _lvFiles.Columns.Add("Expected Hash", 200);
            _lvFiles.Columns.Add("Actual Hash", 200);

            Panel.Controls.Add(_lvFiles);
        }

        public void UpdateProgress(DestinationCopyResult result)
        {
            // Update status
            var (statusText, statusColor) = result.Status switch
            {
                DestinationStatus.Pending => ("⏳ Pending", Color.Gray),
                DestinationStatus.Connecting => ("🔌 Connecting...", Color.FromArgb(52, 152, 219)),
                DestinationStatus.Copying => ("📥 Copying...", Color.FromArgb(52, 152, 219)),
                DestinationStatus.Verifying => ("🔍 Verifying...", Color.FromArgb(230, 126, 34)),
                DestinationStatus.Completed => ("✅ Completed", Color.FromArgb(46, 204, 113)),
                DestinationStatus.CompletedWithErrors => ("⚠️ Completed with errors", Color.FromArgb(230, 126, 34)),
                DestinationStatus.Failed => ("❌ Failed", Color.FromArgb(231, 76, 60)),
                _ => ("Unknown", Color.Gray)
            };

            _lblStatus.Text = statusText;
            _lblStatus.ForeColor = statusColor;

            // Update stats
            var statsText = $"{result.FilesVerified} verified, {result.FilesFailed} failed, {result.FilesCopied}/{result.FilesTotal} copied";
            if (result.Duration.HasValue)
            {
                statsText += $" - Duration: {result.Duration.Value:mm\\:ss}";
            }
            _lblStats.Text = statsText;

            // Update progress bar
            if (result.FilesTotal > 0)
            {
                _progressBar.Maximum = result.FilesTotal;
                _progressBar.Value = Math.Min(
                    result.FilesCopied + result.FilesVerified + result.FilesFailed, 
                    result.FilesTotal);
            }

            // Update files list
            _lvFiles.BeginUpdate();
            foreach (var fileInfo in result.Files)
            {
                if (!_fileItems.TryGetValue(fileInfo.FileName, out var item))
                {
                    item = new ListViewItem(fileInfo.FileName);
                    item.SubItems.Add(FormatFileSize(fileInfo.FileSize));
                    item.SubItems.Add("0.0%");
                    item.SubItems.Add("Pending");
                    item.SubItems.Add(fileInfo.ExpectedHash ?? "N/A");
                    item.SubItems.Add("-");

                    _lvFiles.Items.Add(item);
                    _fileItems[fileInfo.FileName] = item;
                }

                // Update progress
                item.SubItems[2].Text = $"{fileInfo.Progress:F1}%";

                // Update status
                var (fileStatusText, fileStatusColor) = fileInfo.Status switch
                {
                    FileCopyStatus.Pending => ("⏳ Pending", Color.Gray),
                    FileCopyStatus.Copying => ("📥 Copying", Color.Blue),
                    FileCopyStatus.CopyComplete => ("✅ Copied", Color.Green),
                    FileCopyStatus.Verifying => ("🔍 Verifying", Color.Orange),
                    FileCopyStatus.Verified => ("✅ Verified", Color.DarkGreen),
                    FileCopyStatus.HashMismatch => ("❌ Hash Mismatch", Color.Red),
                    FileCopyStatus.Failed => ("❌ Failed", Color.Red),
                    _ => ("Unknown", Color.Gray)
                };

                item.SubItems[3].Text = fileStatusText;
                item.ForeColor = fileStatusColor;

                // Update actual hash
                if (!string.IsNullOrEmpty(fileInfo.ActualHash))
                {
                    item.SubItems[5].Text = fileInfo.ActualHash;

                    // Highlight match/mismatch
                    if (fileInfo.ExpectedHash != null)
                    {
                        if (fileInfo.IsHashMatch)
                        {
                            item.SubItems[5].BackColor = Color.FromArgb(230, 247, 235);
                            item.SubItems[5].ForeColor = Color.FromArgb(39, 174, 96);
                        }
                        else
                        {
                            item.SubItems[5].BackColor = Color.FromArgb(255, 235, 235);
                            item.SubItems[5].ForeColor = Color.FromArgb(231, 76, 60);
                        }
                    }
                }

                // Show error in tooltip
                if (fileInfo.Status == FileCopyStatus.Failed && !string.IsNullOrEmpty(fileInfo.ErrorMessage))
                {
                    item.ToolTipText = $"Error: {fileInfo.ErrorMessage}";
                }
            }
            _lvFiles.EndUpdate();
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
    }
}
