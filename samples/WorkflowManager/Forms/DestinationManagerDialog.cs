using WorkflowManager.Models;

namespace WorkflowManager.Forms;

/// <summary>
/// Dialog để configure destinations cho multi-copy
/// </summary>
public class DestinationManagerDialog : Form
{
    private ListView _lvDestinations = null!;
    private Button _btnAdd = null!;
    private Button _btnEdit = null!;
    private Button _btnRemove = null!;
    private Button _btnOk = null!;
    private Button _btnCancel = null!;

    public List<DestinationInfo> Destinations { get; private set; } = new();

    public DestinationManagerDialog(List<DestinationInfo>? existingDestinations = null)
    {
        if (existingDestinations != null)
        {
            Destinations = existingDestinations.Select(d => new DestinationInfo
            {
                Name = d.Name,
                UncPath = d.UncPath,
                Username = d.Username,
                Password = d.Password,
                IsEnabled = d.IsEnabled
            }).ToList();
        }

        InitializeUI();
        LoadDestinations();
    }

    private void InitializeUI()
    {
        Text = "Manage Copy Destinations";
        Size = new Size(900, 600);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(250, 250, 252);

        // Title
        var lblTitle = new Label
        {
            Text = "📤 Copy Destinations",
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            Location = new Point(15, 15),
            AutoSize = true
        };
        Controls.Add(lblTitle);

        var lblDesc = new Label
        {
            Text = "Configure SMB destinations where packages will be copied",
            Location = new Point(15, 50),
            AutoSize = true,
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.Gray
        };
        Controls.Add(lblDesc);

        // Destinations list
        _lvDestinations = new ListView
        {
            Location = new Point(15, 80),
            Size = new Size(ClientSize.Width - 30, ClientSize.Height - 160),
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            CheckBoxes = true,
            Font = new Font("Segoe UI", 9F)
        };

        _lvDestinations.Columns.Add("Enabled", 70);
        _lvDestinations.Columns.Add("Name", 150);
        _lvDestinations.Columns.Add("UNC Path", 300);
        _lvDestinations.Columns.Add("Username", 150);
        _lvDestinations.Columns.Add("Password", 100);

        _lvDestinations.ItemChecked += (s, e) =>
        {
            if (e.Item.Tag is DestinationInfo dest)
            {
                dest.IsEnabled = e.Item.Checked;
            }
        };

        Controls.Add(_lvDestinations);

        // Buttons panel
        var btnPanel = new Panel
        {
            Location = new Point(15, ClientSize.Height - 70),
            Size = new Size(ClientSize.Width - 30, 50),
            BorderStyle = BorderStyle.None
        };

        _btnAdd = CreateButton("➕ Add", 0, BtnAdd_Click);
        _btnEdit = CreateButton("✏️ Edit", 110, BtnEdit_Click);
        _btnRemove = CreateButton("🗑️ Remove", 220, BtnRemove_Click);

        // Quick Test Setup button
        var btnTest = CreateButton("🧪 Quick Test Setup", 330, BtnTestSetup_Click);
        btnTest.BackColor = Color.FromArgb(155, 89, 182);
        btnTest.ForeColor = Color.White;
        btnTest.FlatStyle = FlatStyle.Flat;
        btnTest.FlatAppearance.BorderSize = 0;

        btnPanel.Controls.AddRange(new Control[] { _btnAdd, _btnEdit, _btnRemove, btnTest });
        Controls.Add(btnPanel);

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
            BackColor = Color.FromArgb(52, 152, 219),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        _btnOk.FlatAppearance.BorderSize = 0;
        Controls.Add(_btnOk);

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;
    }

    private Button CreateButton(string text, int x, EventHandler clickHandler)
    {
        var btn = new Button
        {
            Text = text,
            Size = new Size(100, 35),
            Location = new Point(x, 5),
            Font = new Font("Segoe UI", 9F),
            Cursor = Cursors.Hand
        };
        btn.Click += clickHandler;
        return btn;
    }

    private void LoadDestinations()
    {
        _lvDestinations.Items.Clear();

        foreach (var dest in Destinations)
        {
            var item = new ListViewItem
            {
                Checked = dest.IsEnabled,
                Tag = dest
            };
            item.SubItems.Add(dest.Name);
            item.SubItems.Add(dest.UncPath);
            item.SubItems.Add(dest.Username);
            item.SubItems.Add(new string('*', dest.Password.Length));

            _lvDestinations.Items.Add(item);
        }
    }

    private void BtnAdd_Click(object? sender, EventArgs e)
    {
        using var dialog = new DestinationEditDialog();
        if (dialog.ShowDialog() == DialogResult.OK && dialog.Destination != null)
        {
            Destinations.Add(dialog.Destination);
            LoadDestinations();
        }
    }

    private void BtnEdit_Click(object? sender, EventArgs e)
    {
        if (_lvDestinations.SelectedItems.Count == 0)
        {
            MessageBox.Show("Please select a destination to edit.", "Edit Destination", 
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var item = _lvDestinations.SelectedItems[0];
        var dest = (DestinationInfo)item.Tag;

        using var dialog = new DestinationEditDialog(dest);
        if (dialog.ShowDialog() == DialogResult.OK && dialog.Destination != null)
        {
            dest.Name = dialog.Destination.Name;
            dest.UncPath = dialog.Destination.UncPath;
            dest.Username = dialog.Destination.Username;
            dest.Password = dialog.Destination.Password;
            LoadDestinations();
        }
    }

    private void BtnRemove_Click(object? sender, EventArgs e)
    {
        if (_lvDestinations.SelectedItems.Count == 0)
        {
            MessageBox.Show("Please select a destination to remove.", "Remove Destination",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var item = _lvDestinations.SelectedItems[0];
        var dest = (DestinationInfo)item.Tag;

        var result = MessageBox.Show(
            $"Are you sure you want to remove destination '{dest.Name}'?",
            "Remove Destination",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            Destinations.Remove(dest);
            LoadDestinations();
        }
    }

    private void BtnTestSetup_Click(object? sender, EventArgs e)
    {
        using var dialog = new TestDestinationsDialog();
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var testDests = dialog.Destinations;

            var result = MessageBox.Show(
                $"Add {testDests.Count} test destinations?\n\n" +
                $"This will add:\n" +
                string.Join("\n", testDests.Select(d => $"• {d.Name}: {d.UncPath}")),
                "Add Test Destinations",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                foreach (var dest in testDests)
                {
                    Destinations.Add(dest);
                }

                LoadDestinations();

                MessageBox.Show(
                    $"Added {testDests.Count} test destinations!\n\n" +
                    $"⚠️ Make sure these folders exist on the server:\n" +
                    string.Join("\n", testDests.Select(d => d.UncPath)),
                    "Test Destinations Added",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
    }
}

/// <summary>
/// Dialog để add/edit destination
/// </summary>
public class DestinationEditDialog : Form
{
    private TextBox _txtName = null!;
    private TextBox _txtUncPath = null!;
    private TextBox _txtUsername = null!;
    private TextBox _txtPassword = null!;
    private Button _btnOk = null!;
    private Button _btnCancel = null!;

    public DestinationInfo? Destination { get; private set; }

    public DestinationEditDialog(DestinationInfo? existing = null)
    {
        Destination = existing;
        InitializeUI();

        if (existing != null)
        {
            _txtName.Text = existing.Name;
            _txtUncPath.Text = existing.UncPath;
            _txtUsername.Text = existing.Username;
            _txtPassword.Text = existing.Password;
        }
    }

    private void InitializeUI()
    {
        Text = Destination == null ? "Add Destination" : "Edit Destination";
        Size = new Size(500, 350);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.White;

        var y = 20;

        // Name
        AddLabel("Name:", y);
        _txtName = AddTextBox(y);
        y += 60;

        // UNC Path
        AddLabel("UNC Path:", y);
        _txtUncPath = AddTextBox(y);
        _txtUncPath.PlaceholderText = "\\\\server\\share\\path";
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

        // Buttons
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
            BackColor = Color.FromArgb(52, 152, 219),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _btnOk.FlatAppearance.BorderSize = 0;
        _btnOk.Click += BtnOk_Click;
        Controls.Add(_btnOk);

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;
    }

    private void AddLabel(string text, int y)
    {
        var lbl = new Label
        {
            Text = text,
            Location = new Point(20, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };
        Controls.Add(lbl);
    }

    private TextBox AddTextBox(int y)
    {
        var txt = new TextBox
        {
            Location = new Point(20, y + 25),
            Size = new Size(ClientSize.Width - 40, 25),
            Font = new Font("Segoe UI", 9F)
        };
        Controls.Add(txt);
        return txt;
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        // Validate
        if (string.IsNullOrWhiteSpace(_txtName.Text))
        {
            MessageBox.Show("Please enter a name.", "Validation", 
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_txtUncPath.Text))
        {
            MessageBox.Show("Please enter a UNC path.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!_txtUncPath.Text.StartsWith("\\\\"))
        {
            MessageBox.Show("UNC path must start with \\\\", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_txtUsername.Text))
        {
            MessageBox.Show("Please enter a username.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Destination = new DestinationInfo
        {
            Name = _txtName.Text.Trim(),
            UncPath = _txtUncPath.Text.Trim(),
            Username = _txtUsername.Text.Trim(),
            Password = _txtPassword.Text,
            IsEnabled = true
        };

        DialogResult = DialogResult.OK;
        Close();
    }
}
