using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Core.Models;
using SmbEnterprise.WinFormsApp.Services;

namespace SmbEnterprise.WinFormsApp;

public sealed class FullTestForm : Form
{
    // ─── Controls ────────────────────────────────────────────────────────────

    private TextBox    tbServer    = null!;
    private TextBox    tbShare     = null!;
    private TextBox    tbUser      = null!;
    private TextBox    tbPass      = null!;
    private TextBox    tbSourceDir = null!;
    private Button     btnRun      = null!;
    private Button     btnCancel   = null!;
    private ProgressBar pbOverall  = null!;
    private Label      lblStatus   = null!;
    private ListView   lvSections  = null!;
    private RichTextBox rtbLog     = null!;

    // ─── State ────────────────────────────────────────────────────────────────

    private readonly IServiceProvider _sp;
    private readonly UiLogStore       _logStore;
    private CancellationTokenSource?  _cts;
    private bool _running;

    private static readonly string[] SectionNames =
    [
        "1. Core filesystem ops",
        "2. TransferEngine – SMB → local",
        "3. TransferEngine – resume",
        "4. TransferEngine – multi-destination",
        "5. TransferEngine – directory",
        "6. ChecksumEngine – 4 algorithms",
        "7. TransferVerifier – hash verify",
        "8. MetadataCache – set/get/TTL",
        "9. ReadAheadPrefetcher",
        "10. AdaptiveChunkSizer",
        "11. InMemoryJobQueue",
        "12. SqliteJobRepository",
        "13. TransferTelemetry",
        "14. TransferDashboard"
    ];

    // ─── Constructor ─────────────────────────────────────────────────────────

    public FullTestForm(IServiceProvider sp, UiLogStore logStore)
    {
        _sp       = sp;
        _logStore = logStore;

        Text            = "Full Feature Test";
        StartPosition   = FormStartPosition.CenterParent;
        Width           = 1100;
        Height          = 780;
        MinimumSize     = new Size(900, 600);

        InitializeComponent();
        PopulateSectionRows();
        WireEvents();
    }

    // ─── Build UI ─────────────────────────────────────────────────────────────

    private void InitializeComponent()
    {
        var root = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 5,
            Padding     = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));   // settings
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));   // progress + status
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));    // section list
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));   // log label
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));    // log

        root.Controls.Add(BuildSettingsPanel(), 0, 0);
        root.Controls.Add(BuildProgressPanel(), 0, 1);
        root.Controls.Add(BuildSectionList(),   0, 2);
        root.Controls.Add(new Label { Text = "Logs", Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold) }, 0, 3);
        root.Controls.Add(BuildLogPanel(),      0, 4);

        Controls.Add(root);
    }

    private Panel BuildSettingsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 6,
            RowCount    = 2
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  20));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  15));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  65));

        // Row 0: Server / Share / SourceDir
        panel.Controls.Add(Lbl("Server:"),  0, 0);
        tbServer = TB("192.168.1.250");
        panel.Controls.Add(tbServer, 1, 0);
        panel.Controls.Add(Lbl("Share:"),   2, 0);
        tbShare  = TB("media");
        panel.Controls.Add(tbShare,  3, 0);
        panel.Controls.Add(Lbl("Src Dir:"), 4, 0);
        tbSourceDir = TB(@"\movies\2002 2001 DD2 0 Chan10Bit");
        panel.Controls.Add(tbSourceDir, 5, 0);

        // Row 1: User / Pass / Buttons
        panel.Controls.Add(Lbl("User:"),    0, 1);
        tbUser = TB("share");
        panel.Controls.Add(tbUser, 1, 1);
        panel.Controls.Add(Lbl("Pass:"),    2, 1);
        tbPass = TB("", isPassword: true);
        tbPass.Text = "1234567890";
        panel.Controls.Add(tbPass, 3, 1);

        var btnPanel = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            RowCount    = 1
        };
        btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));

        btnRun = new Button
        {
            Text      = "▶  Run Full Test",
            Width     = 140,
            Height    = 28,
            Margin    = new Padding(0, 2, 8, 0),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font      = new Font(Font, FontStyle.Bold)
        };
        btnCancel = new Button
        {
            Text      = "⬛  Cancel",
            Width     = 100,
            Height    = 28,
            Margin    = new Padding(0, 2, 0, 0),
            Enabled   = false
        };

        btnRun.Margin    = new Padding(0, 2, 4, 0);
        btnCancel.Margin = new Padding(0, 2, 0, 0);

        btnPanel.Controls.Add(btnRun, 0, 0);
        btnPanel.Controls.Add(btnCancel, 1, 0);
        panel.Controls.Add(btnPanel, 4, 1);
        panel.SetColumnSpan(btnPanel, 2);

        return panel;
    }

    private Panel BuildProgressPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            RowCount    = 1
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));

        pbOverall = new ProgressBar
        {
            Dock    = DockStyle.Fill,
            Minimum = 0,
            Maximum = SectionNames.Length,
            Style   = ProgressBarStyle.Continuous,
            Margin  = new Padding(0, 4, 8, 4)
        };

        lblStatus = new Label
        {
            Dock      = DockStyle.Fill,
            Text      = "Sẵn sàng",
            TextAlign = ContentAlignment.MiddleLeft
        };

        panel.Controls.Add(pbOverall,  0, 0);
        panel.Controls.Add(lblStatus,  1, 0);
        return panel;
    }

    private ListView BuildSectionList()
    {
        lvSections = new ListView
        {
            Dock          = DockStyle.Fill,
            View          = View.Details,
            FullRowSelect = true,
            GridLines     = true,
            HeaderStyle   = ColumnHeaderStyle.Nonclickable
        };
        lvSections.Columns.Add("#",       32,  HorizontalAlignment.Center);
        lvSections.Columns.Add("Section", 290, HorizontalAlignment.Left);
        lvSections.Columns.Add("Status",  80,  HorizontalAlignment.Center);
        lvSections.Columns.Add("Time",    68,  HorizontalAlignment.Right);
        lvSections.Columns.Add("Detail",  -2,  HorizontalAlignment.Left);
        return lvSections;
    }

    private RichTextBox BuildLogPanel()
    {
        rtbLog = new RichTextBox
        {
            Dock      = DockStyle.Fill,
            ReadOnly  = true,
            BackColor = Color.FromArgb(18, 18, 18),
            ForeColor = Color.LightGray,
            Font      = new Font("Consolas", 8.5f),
            ScrollBars = RichTextBoxScrollBars.Vertical
        };
        return rtbLog;
    }

    private void PopulateSectionRows()
    {
        lvSections.Items.Clear();
        for (var i = 0; i < SectionNames.Length; i++)
        {
            var item = new ListViewItem((i + 1).ToString());
            item.SubItems.Add(SectionNames[i]);
            item.SubItems.Add("─");
            item.SubItems.Add("");
            item.SubItems.Add("");
            item.ForeColor = Color.Gray;
            lvSections.Items.Add(item);
        }
    }

    // ─── Events ──────────────────────────────────────────────────────────────

    private void WireEvents()
    {
        btnRun.Click    += BtnRun_Click;
        btnCancel.Click += BtnCancel_Click;
        FormClosing     += (_, _) => _cts?.Cancel();
        _logStore.LogAdded += LogStore_LogAdded;
    }

    private void LogStore_LogAdded(object? sender, string line)
    {
        AppendLog(line, GetLogColor(line));
    }

    private void AppendLog(string text, Color color)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendLog(text, color)); return; }
        rtbLog.SelectionStart  = rtbLog.TextLength;
        rtbLog.SelectionLength = 0;
        rtbLog.SelectionColor  = color;
        rtbLog.AppendText(text + "\n");
        rtbLog.ScrollToCaret();
    }

    private static Color GetLogColor(string line) =>
        line.Contains("[ERR]") || line.Contains("[Fatal]") ? Color.Tomato  :
        line.Contains("[WRN]")                             ? Color.Yellow   :
        line.Contains("[DBG]")                             ? Color.DimGray  :
        Color.LightGray;

    // ─── Run / Cancel ─────────────────────────────────────────────────────────

    private void BtnRun_Click(object? sender, EventArgs e)
    {
        if (_running) return;
        _running = true;
        _cts     = new CancellationTokenSource();
        btnRun.Enabled    = false;
        btnCancel.Enabled = true;
        pbOverall.Value   = 0;
        lblStatus.Text    = "Running…";

        PopulateSectionRows();
        rtbLog.Clear();
        AppendLog($"[{DateTime.Now:HH:mm:ss}] ▶ Starting Full Feature Test", Color.Cyan);

        var settings = new TestSettings(
            Server    : tbServer.Text.Trim(),
            Share     : tbShare.Text.Trim(),
            Username  : tbUser.Text.Trim(),
            Password  : tbPass.Text,
            SourceDir : tbSourceDir.Text.Trim());

        var runner = new FullTestRunner(
            _sp.GetRequiredService<ILoggerFactory>(),
            _sp.GetRequiredService<IFileSystemProvider>());

        var sectionProgress  = new Progress<SectionResult>(OnSectionResult);
        var transferProgress = new Progress<TransferProgress>(OnTransferProgress);
        var ct = _cts.Token;

        Task.Run(async () =>
        {
            try
            {
                await runner.RunAsync(settings, sectionProgress, transferProgress, ct);
                Invoke(OnTestComplete);
            }
            catch (OperationCanceledException)
            {
                Invoke(() => OnTestAborted("Đã huỷ bởi người dùng"));
            }
            catch (Exception ex)
            {
                Invoke(() => OnTestAborted($"Lỗi: {ex.Message}"));
            }
        }, ct);
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        _cts?.Cancel();
        btnCancel.Enabled = false;
        lblStatus.Text    = "Đang huỷ…";
    }

    // ─── Progress callbacks ───────────────────────────────────────────────────

    private void OnSectionResult(SectionResult r)
    {
        if (InvokeRequired) { BeginInvoke(() => OnSectionResult(r)); return; }

        var idx  = r.Index - 1;
        if (idx < 0 || idx >= lvSections.Items.Count) return;
        var item = lvSections.Items[idx];

        item.SubItems[0].Text = r.Index.ToString();
        item.SubItems[2].Text = r.Status switch
        {
            SectionStatus.Pass    => "PASS",
            SectionStatus.Fail    => "FAIL",
            SectionStatus.Running => "…",
            SectionStatus.Skipped => "SKIP",
            _                     => "─"
        };
        item.SubItems[3].Text = r.Duration.TotalSeconds > 0
            ? (r.Duration.TotalSeconds < 60 ? $"{r.Duration.TotalSeconds:F1}s" : $"{r.Duration:m\\:ss}")
            : "";
        item.SubItems[4].Text = r.Detail;

        (item.BackColor, item.ForeColor) = r.Status switch
        {
            SectionStatus.Pass    => (Color.FromArgb(20, 80, 20), Color.LightGreen),
            SectionStatus.Fail    => (Color.FromArgb(80, 20, 20), Color.Tomato),
            SectionStatus.Running => (Color.FromArgb(30, 60, 100), Color.DeepSkyBlue),
            SectionStatus.Skipped => (Color.FromArgb(50, 50, 50), Color.DarkGray),
            _                     => (lvSections.BackColor, Color.Gray)
        };

        if (r.Status == SectionStatus.Pass || r.Status == SectionStatus.Fail)
        {
            pbOverall.Value = Math.Min(pbOverall.Maximum,
                lvSections.Items.Cast<ListViewItem>()
                    .Count(i => i.SubItems[2].Text is "PASS" or "FAIL" or "SKIP"));

            var logColor = r.Status == SectionStatus.Pass ? Color.LightGreen : Color.Tomato;
            var mark     = r.Status == SectionStatus.Pass ? "✔" : "✘";
            AppendLog($"  [{mark}] {r.Name}: {r.Detail}", logColor);
        }
        else if (r.Status == SectionStatus.Running)
        {
            AppendLog($"\n══ {r.Name} ══", Color.Cyan);
            lblStatus.Text = $"[{r.Index}/{SectionNames.Length}] {r.Name}";
        }

        lvSections.EnsureVisible(idx);
    }

    private string _lastTransferText = string.Empty;

    private void OnTransferProgress(TransferProgress p)
    {
        if (InvokeRequired) { BeginInvoke(() => OnTransferProgress(p)); return; }

        var pct   = p.TotalBytes > 0 ? p.TransferredBytes * 100.0 / p.TotalBytes : 0;
        var speed = p.SpeedBytesPerSecond / 1024.0 / 1024.0;
        var text  = $"Transfer: {pct:F1}%  |  {speed:F1} MB/s  |  {p.TransferredBytes / 1024.0 / 1024:F0}/{p.TotalBytes / 1024.0 / 1024:F0} MB";
        if (text != _lastTransferText)
        {
            lblStatus.Text    = text;
            _lastTransferText = text;
        }
    }

    private void OnTestComplete()
    {
        _running          = false;
        btnRun.Enabled    = true;
        btnCancel.Enabled = false;
        pbOverall.Value   = pbOverall.Maximum;

        var pass = lvSections.Items.Cast<ListViewItem>().Count(i => i.SubItems[2].Text == "PASS");
        var fail = lvSections.Items.Cast<ListViewItem>().Count(i => i.SubItems[2].Text == "FAIL");

        lblStatus.Text = $"Hoàn thành: {pass} PASS, {fail} FAIL";
        AppendLog($"\n[{DateTime.Now:HH:mm:ss}] ══ Test hoàn tất: {pass} PASS  {fail} FAIL ══",
            fail > 0 ? Color.Orange : Color.LightGreen);
    }

    private void OnTestAborted(string reason)
    {
        _running          = false;
        btnRun.Enabled    = true;
        btnCancel.Enabled = false;
        lblStatus.Text    = reason;
        AppendLog($"\n[{DateTime.Now:HH:mm:ss}] ⬛ {reason}", Color.Orange);
    }

    // ─── Static helpers ───────────────────────────────────────────────────────

    private static Label Lbl(string text) => new Label
    {
        Text      = text,
        Dock      = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight,
        Padding   = new Padding(0, 0, 4, 0)
    };

    private static TextBox TB(string text, bool isPassword = false) => new TextBox
    {
        Dock                  = DockStyle.Fill,
        Text                  = text,
        UseSystemPasswordChar = isPassword,
        Margin                = new Padding(0, 3, 6, 3)
    };
}
