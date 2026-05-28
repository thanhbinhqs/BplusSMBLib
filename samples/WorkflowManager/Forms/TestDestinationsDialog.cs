using WorkflowManager.Models;

namespace WorkflowManager.Forms;

/// <summary>
/// Dialog để quick setup test destinations
/// </summary>
public class TestDestinationsDialog : Form
{
    private TextBox _txtServer = null!;
    private TextBox _txtShare = null!;
    private TextBox _txtBasePath = null!;
    private TextBox _txtUsername = null!;
    private TextBox _txtPassword = null!;
    private NumericUpDown _numCount = null!;
    private TextBox _txtPrefix = null!;
    private Button _btnGenerate = null!;
    private Button _btnOk = null!;
    private Button _btnCancel = null!;
    private ListView _lvPreview = null!;

    public List<DestinationInfo> Destinations { get; private set; } = new();

    public TestDestinationsDialog()
    {
        InitializeUI();
        SetDefaults();
    }

    private void InitializeUI()
    {
        Text = "Setup Test Destinations";
        Size = new Size(800, 600);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(250, 250, 252);

        // Title
        var lblTitle = new Label
        {
            Text = "🧪 Quick Test Destinations Setup",
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            Location = new Point(15, 15),
            AutoSize = true
        };
        Controls.Add(lblTitle);

        var lblDesc = new Label
        {
            Text = "Generate multiple test destinations with the same credentials but different folders",
            Location = new Point(15, 50),
            AutoSize = true,
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.Gray
        };
        Controls.Add(lblDesc);

        var y = 80;

        // Server
        AddLabel("Server:", y);
        _txtServer = AddTextBox(y);
        y += 60;

        // Share
        AddLabel("Share:", y);
        _txtShare = AddTextBox(y);
        y += 60;

        // Base Path (optional)
        AddLabel("Base Path (optional):", y);
        _txtBasePath = AddTextBox(y);
        _txtBasePath.PlaceholderText = "Leave empty for root of share";
        y += 60;

        // Username
        AddLabel("Username:", y);
        _txtUsername = AddTextBox(y);
        y += 60;

        // Password
        AddLabel("Password:", y);
        _txtPassword = AddTextBox(y);
        _txtPassword.PasswordChar = '●';
        y += 60;

        // Number of destinations
        AddLabel("Number of Destinations:", y);
        _numCount = new NumericUpDown
        {
            Location = new Point(180, y + 25),
            Size = new Size(100, 25),
            Font = new Font("Segoe UI", 9F),
            Minimum = 1,
            Maximum = 20,
            Value = 5
        };
        Controls.Add(_numCount);
        y += 60;

        // Folder prefix
        AddLabel("Folder Prefix:", y);
        _txtPrefix = AddTextBox(y);
        _txtPrefix.Text = "A";
        var lblPrefixNote = new Label
        {
            Text = "(Will create: A1, A2, A3, ...)",
            Location = new Point(490, y + 28),
            AutoSize = true,
            Font = new Font("Segoe UI", 8F),
            ForeColor = Color.Gray
        };
        Controls.Add(lblPrefixNote);
        y += 60;

        // Generate button
        _btnGenerate = new Button
        {
            Text = "🔄 Generate Preview",
            Location = new Point(15, y),
            Size = new Size(150, 35),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            BackColor = Color.FromArgb(52, 152, 219),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnGenerate.FlatAppearance.BorderSize = 0;
        _btnGenerate.Click += BtnGenerate_Click;
        Controls.Add(_btnGenerate);
        y += 50;

        // Preview list
        var lblPreview = new Label
        {
            Text = "Preview:",
            Location = new Point(15, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };
        Controls.Add(lblPreview);
        y += 25;

        _lvPreview = new ListView
        {
            Location = new Point(15, y),
            Size = new Size(ClientSize.Width - 30, 120),
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            Font = new Font("Segoe UI", 9F)
        };
        _lvPreview.Columns.Add("Name", 100);
        _lvPreview.Columns.Add("UNC Path", 500);
        Controls.Add(_lvPreview);

        // OK/Cancel buttons
        _btnCancel = new Button
        {
            Text = "Cancel",
            Size = new Size(100, 35),
            Location = new Point(ClientSize.Width - 225, ClientSize.Height - 50),
            Font = new Font("Segoe UI", 9F),
            DialogResult = DialogResult.Cancel
        };
        Controls.Add(_btnCancel);

        _btnOk = new Button
        {
            Text = "OK",
            Size = new Size(100, 35),
            Location = new Point(ClientSize.Width - 115, ClientSize.Height - 50),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            BackColor = Color.FromArgb(39, 174, 96),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Enabled = false
        };
        _btnOk.FlatAppearance.BorderSize = 0;
        _btnOk.Click += BtnOk_Click;
        Controls.Add(_btnOk);

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;
    }

    private void SetDefaults()
    {
        _txtServer.Text = "192.168.1.250";
        _txtShare.Text = "share";
        _txtBasePath.Text = "";
        _txtUsername.Text = "share";
        _txtPassword.Text = "1234567890";
        _numCount.Value = 5;
        _txtPrefix.Text = "A";
    }

    private void AddLabel(string text, int y)
    {
        var lbl = new Label
        {
            Text = text,
            Location = new Point(15, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };
        Controls.Add(lbl);
    }

    private TextBox AddTextBox(int y)
    {
        var txt = new TextBox
        {
            Location = new Point(180, y + 25),
            Size = new Size(300, 25),
            Font = new Font("Segoe UI", 9F)
        };
        Controls.Add(txt);
        return txt;
    }

    private void BtnGenerate_Click(object? sender, EventArgs e)
    {
        // Validate
        if (string.IsNullOrWhiteSpace(_txtServer.Text))
        {
            MessageBox.Show("Please enter server address.", "Validation", 
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_txtShare.Text))
        {
            MessageBox.Show("Please enter share name.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_txtUsername.Text))
        {
            MessageBox.Show("Please enter username.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_txtPrefix.Text))
        {
            MessageBox.Show("Please enter folder prefix.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Generate destinations
        Destinations.Clear();
        _lvPreview.Items.Clear();

        var server = _txtServer.Text.Trim();
        var share = _txtShare.Text.Trim();
        var basePath = _txtBasePath.Text.Trim();
        var username = _txtUsername.Text.Trim();
        var password = _txtPassword.Text;
        var count = (int)_numCount.Value;
        var prefix = _txtPrefix.Text.Trim();

        for (int i = 1; i <= count; i++)
        {
            var folderName = $"{prefix}{i}";

            // Build UNC path
            var uncPath = $"\\\\{server}\\{share}";
            if (!string.IsNullOrEmpty(basePath))
            {
                uncPath += $"\\{basePath.TrimStart('\\')}";
            }
            uncPath += $"\\{folderName}";

            var dest = new DestinationInfo
            {
                Name = folderName,
                UncPath = uncPath,
                Username = username,
                Password = password,
                IsEnabled = true
            };

            Destinations.Add(dest);

            // Add to preview
            var item = new ListViewItem(dest.Name);
            item.SubItems.Add(dest.UncPath);
            _lvPreview.Items.Add(item);
        }

        _btnOk.Enabled = true;

        MessageBox.Show(
            $"Generated {count} test destinations!\n\n" +
            $"Make sure the folders {prefix}1 to {prefix}{count} are accessible on the server.",
            "Preview Generated",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (Destinations.Count == 0)
        {
            MessageBox.Show("Please generate destinations first.", "No Destinations",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
