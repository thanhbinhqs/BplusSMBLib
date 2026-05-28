using WorkflowManager.Models;

namespace WorkflowManager.Forms;

/// <summary>
/// Dialog hiển thị chi tiết quá trình copy và verify
/// </summary>
public sealed class CopyProgressDialog : Form
{
    private ListView _lvFiles = null!;
    private ProgressBar _overallProgress = null!;
    private Label _lblStatus = null!;
    private Label _lblOverallProgress = null!;
    private Button _btnClose = null!;
    private readonly Dictionary<string, ListViewItem> _fileItems = new();

    public bool IsCancelled { get; private set; }

    public CopyProgressDialog(string packageName, int totalFiles)
    {
        InitializeUI(packageName, totalFiles);
    }

    private void InitializeUI(string packageName, int totalFiles)
    {
        Text = $"Copying Package: {packageName}";
        Size = new Size(1400, 700);
        MinimumSize = new Size(1000, 500);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable; // Allow resize
        MaximizeBox = true;
        MinimizeBox = true;
        BackColor = Color.FromArgb(240, 240, 245);
        Font = new Font("Segoe UI", 9F);

        // Title
        var titleLabel = new Label
        {
            Text = $"📦 Copying Package: {packageName}",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = Color.FromArgb(41, 128, 185),
            AutoSize = true,
            Location = new Point(15, 15)
        };
        Controls.Add(titleLabel);

        // Overall progress label
        _lblOverallProgress = new Label
        {
            Text = $"0 / {totalFiles} files completed",
            Location = new Point(15, 50),
            AutoSize = true,
            Font = new Font("Segoe UI", 9F),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        Controls.Add(_lblOverallProgress);

        // Overall progress bar
        _overallProgress = new ProgressBar
        {
            Location = new Point(15, 75),
            Size = new Size(ClientSize.Width - 30, 25),
            Style = ProgressBarStyle.Continuous,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        Controls.Add(_overallProgress);

        // Files ListView
        _lvFiles = new ListView
        {
            Location = new Point(15, 110),
            Size = new Size(ClientSize.Width - 30, ClientSize.Height - 220),
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            Font = new Font("Segoe UI", 9F),
            BorderStyle = BorderStyle.FixedSingle,
            ShowItemToolTips = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        _lvFiles.Columns.Add("File Name", 300);
        _lvFiles.Columns.Add("Size", 100);
        _lvFiles.Columns.Add("Progress", 100);
        _lvFiles.Columns.Add("Status", 130);
        _lvFiles.Columns.Add("Expected Hash", 250);
        _lvFiles.Columns.Add("Actual Hash", 250);

        // Custom draw for headers
        _lvFiles.OwnerDraw = true;
        _lvFiles.DrawColumnHeader += (s, e) =>
        {
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(52, 73, 94)), e.Bounds);
            var headerFont = new Font("Segoe UI", 9F, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, headerFont,
                e.Bounds, Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
        _lvFiles.DrawItem += (s, e) => e.DrawDefault = true;
        _lvFiles.DrawSubItem += (s, e) => e.DrawDefault = true;

        Controls.Add(_lvFiles);

        // Status label
        _lblStatus = new Label
        {
            Text = "Initializing...",
            Location = new Point(15, ClientSize.Height - 90),
            Size = new Size(ClientSize.Width - 30, 20),
            Font = new Font("Segoe UI", 9F),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        Controls.Add(_lblStatus);

        // Close button
        _btnClose = new Button
        {
            Text = "Cancel",
            Location = new Point(ClientSize.Width - 115, ClientSize.Height - 55),
            Size = new Size(100, 30),
            BackColor = Color.FromArgb(192, 57, 43),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Enabled = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _btnClose.FlatAppearance.BorderSize = 0;
        _btnClose.Click += (s, e) =>
        {
            if (_btnClose.Text == "Close")
            {
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                IsCancelled = true;
                _btnClose.Enabled = false;
                _btnClose.Text = "Cancelling...";
                _lblStatus.Text = "⏸️ Cancelling operation...";
            }
        };
        Controls.Add(_btnClose);
    }

    public void AddFile(FileCopyInfo fileInfo)
    {
        if (InvokeRequired)
        {
            Invoke(() => AddFile(fileInfo));
            return;
        }

        // Check if file already exists in the list
        if (_fileItems.ContainsKey(fileInfo.FileName))
        {
            return; // Already added, just skip
        }

        var item = new ListViewItem(fileInfo.FileName);
        item.SubItems.Add(FormatFileSize(fileInfo.FileSize));
        item.SubItems.Add("0%");
        item.SubItems.Add("⏳ Pending");
        item.SubItems.Add(fileInfo.ExpectedHash ?? "N/A");
        item.SubItems.Add("-");
        item.Tag = fileInfo;

        _lvFiles.Items.Add(item);
        _fileItems[fileInfo.FileName] = item;
    }

    public void UpdateFileProgress(string fileName, FileCopyInfo fileInfo)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateFileProgress(fileName, fileInfo));
            return;
        }

        if (!_fileItems.TryGetValue(fileName, out var item))
            return;

        item.SubItems[2].Text = $"{fileInfo.Progress:F1}%";

        var (statusText, statusColor) = fileInfo.Status switch
        {
            FileCopyStatus.Pending => ("⏳ Pending", Color.Gray),
            FileCopyStatus.Copying => ("📥 Copying", Color.Blue),
            FileCopyStatus.CopyComplete => ("✅ Copied", Color.Green),
            FileCopyStatus.Verifying => ("🔍 Verifying", Color.Orange),
            FileCopyStatus.Verified => ("✅ Verified", Color.DarkGreen),
            FileCopyStatus.HashMismatch => ("❌ Hash Mismatch", Color.Red),
            FileCopyStatus.Failed => ($"❌ Failed", Color.Red),
            _ => ("Unknown", Color.Gray)
        };

        item.SubItems[3].Text = statusText;
        item.ForeColor = statusColor;

        // Show error message in tooltip if failed
        if (fileInfo.Status == FileCopyStatus.Failed && !string.IsNullOrEmpty(fileInfo.ErrorMessage))
        {
            item.ToolTipText = $"Error: {fileInfo.ErrorMessage}";
        }

        if (!string.IsNullOrEmpty(fileInfo.ActualHash))
        {
            item.SubItems[5].Text = fileInfo.ActualHash;

            // Highlight hash match/mismatch
            if (fileInfo.ExpectedHash != null)
            {
                if (fileInfo.IsHashMatch)
                {
                    item.SubItems[5].BackColor = Color.FromArgb(230, 247, 235);
                    item.SubItems[5].ForeColor = Color.FromArgb(39, 174, 96);
                }
                else
                {
                    item.SubItems[5].BackColor = Color.FromArgb(252, 236, 236);
                    item.SubItems[5].ForeColor = Color.FromArgb(192, 57, 43);
                }
            }
        }
    }

    public void UpdateOverallProgress(int completed, int total)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateOverallProgress(completed, total));
            return;
        }

        _lblOverallProgress.Text = $"{completed} / {total} files completed";
        _overallProgress.Maximum = total;
        _overallProgress.Value = Math.Min(completed, total);
    }

    public void UpdateStatus(string status)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateStatus(status));
            return;
        }

        _lblStatus.Text = status;
    }

    public void SetComplete(bool success, string message)
    {
        if (InvokeRequired)
        {
            Invoke(() => SetComplete(success, message));
            return;
        }

        _lblStatus.Text = message;
        _btnClose.Text = "Close";
        _btnClose.BackColor = success 
            ? Color.FromArgb(39, 174, 96) 
            : Color.FromArgb(192, 57, 43);
        _btnClose.Enabled = true;
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
