namespace SmbEnterprise.WinFormsApp;

public class ConnectDialog : Form
{
    public SmbSettings Settings { get; private set; }

    private TextBox tbServer = null!;
    private TextBox tbShare = null!;
    private TextBox tbUsername = null!;
    private TextBox tbPassword = null!;
    private NumericUpDown nudPort = null!;

    public ConnectDialog(SmbSettings current)
    {
        Settings = new SmbSettings
        {
            Server = current.Server,
            Share = current.Share,
            Username = current.Username,
            Port = current.Port > 0 ? current.Port : 445
        };

        InitializeDialog();

        tbServer.Text = current.Server;
        tbShare.Text = current.Share;
        tbUsername.Text = current.Username;
        nudPort.Value = current.Port > 0 ? current.Port : 445;
    }

    private void InitializeDialog()
    {
        this.Text = "Connect to SMB Server";
        this.Size = new Size(420, 300);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.Font = new Font("Segoe UI", 9f);
        this.BackColor = Color.White;

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0)
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        // Form fields
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(24, 20, 24, 8)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++)
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        tbServer = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
        tbShare = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
        tbUsername = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
        tbPassword = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4), PasswordChar = '●' };
        nudPort = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 4),
            Minimum = 1,
            Maximum = 65535,
            Value = 445
        };

        void AddRow(int row, string labelText, Control ctrl)
        {
            var lbl = new Label
            {
                Text = labelText,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(60, 60, 60)
            };
            grid.Controls.Add(lbl, 0, row);
            grid.Controls.Add(ctrl, 1, row);
        }

        AddRow(0, "Server:", tbServer);
        AddRow(1, "Share:", tbShare);
        AddRow(2, "Username:", tbUsername);
        AddRow(3, "Password:", tbPassword);
        AddRow(4, "Port:", nudPort);

        outer.Controls.Add(grid, 0, 0);

        // Button bar at bottom
        var btnBar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(243, 243, 243),
            Padding = new Padding(16, 10, 16, 10)
        };
        btnBar.Paint += (s, e) =>
            e.Graphics.DrawLine(new Pen(Color.FromArgb(220, 220, 220)), 0, 0, btnBar.Width, 0);

        var btnOk = new Button
        {
            Text = "Connect",
            Width = 90,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 120, 212),
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        btnOk.FlatAppearance.BorderSize = 0;
        btnOk.Click += BtnOk_Click;

        var btnCancel = new Button
        {
            Text = "Cancel",
            Width = 80,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.Cancel,
            Cursor = Cursors.Hand
        };
        btnCancel.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0)
        };
        flow.Controls.Add(btnOk);
        flow.Controls.Add(btnCancel);
        btnBar.Controls.Add(flow);

        outer.Controls.Add(btnBar, 0, 1);
        this.Controls.Add(outer);
        this.AcceptButton = btnOk;
        this.CancelButton = btnCancel;
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(tbServer.Text))
        {
            MessageBox.Show("Server is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(tbShare.Text))
        {
            MessageBox.Show("Share is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Settings = new SmbSettings
        {
            Server = tbServer.Text.Trim(),
            Share = tbShare.Text.Trim(),
            Username = tbUsername.Text.Trim(),
            Port = (int)nudPort.Value
        };

        // Store password separately (not in SmbSettings for persistence)
        Settings.Password = tbPassword.Text;

        this.DialogResult = DialogResult.OK;
    }
}
