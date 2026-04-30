using System.Diagnostics;
using System.Drawing;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using SyntheticHttpMonitor.Options;

namespace SyntheticHttpMonitor.ConfigEditor;

internal sealed class MainForm : Form
{
    private enum ServiceRestartOutcome
    {
        NotRequested,
        RestartedInSession,
        RestartedElevated,
        RestartCancelledByUser,
        RestartFailed,
    }

    private string? _currentPath;
    private JsonObject _root = new();

    private readonly NumericUpDown _defInterval = new() { Minimum = 5, Maximum = 86_400, Width = 100 };
    private readonly NumericUpDown _defTimeout = new() { Minimum = 1, Maximum = 600, Width = 100 };
    private readonly TextBox _defCodes = new() { Width = 120, PlaceholderText = "200, 204" };
    private readonly NumericUpDown _defMaxBody = new() { Minimum = 1024, Maximum = 50_000_000, Increment = 1024, Width = 120 };
    private readonly CheckBox _defSkipCert = new()
    {
        AutoSize = true,
        Text = "Default: skip HTTPS certificate validation",
    };

    private readonly DataGridView _targetsGrid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
        ScrollBars = ScrollBars.Both,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
    };

    private readonly NumericUpDown _alertFailThreshold = new() { Minimum = 1, Maximum = 100, Width = 80 };
    private readonly NumericUpDown _alertRepeatMin = new() { Minimum = 0, Maximum = 10_080, Width = 80 };
    private readonly CheckBox _alertRecovery = new() { AutoSize = true, Text = "Send recovery email when a check succeeds again" };

    private readonly CheckBox _smtpEnabled = new() { AutoSize = true, Text = "Enable email alerts" };
    private readonly TextBox _smtpHost = new() { Width = 280 };
    private readonly NumericUpDown _smtpPort = new() { Minimum = 1, Maximum = 65535, Width = 80 };
    private readonly CheckBox _smtpSsl = new() { AutoSize = true, Text = "Use SSL (implicit, e.g. port 465)" };
    private readonly CheckBox _smtpStartTls = new() { AutoSize = true, Text = "Use STARTTLS" };
    private readonly TextBox _smtpUser = new() { Width = 200 };
    private readonly TextBox _smtpPass = new() { Width = 200, UseSystemPasswordChar = true };
    private readonly TextBox _smtpFrom = new() { Width = 280 };
    private readonly TextBox _smtpTo = new() { Width = 400, PlaceholderText = "Comma-separated addresses" };
    private readonly TextBox _smtpCc = new() { Width = 400, PlaceholderText = "Optional, comma-separated" };
    private readonly TextBox _smtpSubDown = new() { Width = 120 };
    private readonly TextBox _smtpSubRec = new() { Width = 120 };

    private readonly CheckBox _tickEnabled = new() { AutoSize = true, Text = "Enable ticketing API (reserved — not used by the service yet)" };
    private readonly TextBox _tickBaseUrl = new() { Width = 400 };
    private readonly TextBox _tickHeader = new() { Width = 160 };
    private readonly TextBox _tickApiKey = new() { Width = 280, UseSystemPasswordChar = true };
    private readonly TextBox _tickProject = new() { Width = 160 };

    private readonly CheckBox _pdEnabled = new() { AutoSize = true, Text = "Send PagerDuty Events API alerts when a check is down" };
    private readonly TextBox _pdRoutingKey = new() { Width = 520, UseSystemPasswordChar = true };
    private readonly TextBox _pdEventsUrl = new() { Width = 520 };
    private readonly TextBox _pdSeverity = new() { Width = 120 };
    private readonly TextBox _pdSource = new() { Width = 280, PlaceholderText = "Empty = this computer name" };
    private readonly CheckBox _pdResolveOnRecovery = new()
    {
        AutoSize = true,
        Text = "Send resolve to PagerDuty when the check succeeds again (same dedup key as trigger)",
    };

    private readonly ToolStripStatusLabel _serviceStatusLabel = new()
    {
        AutoSize = true,
        Margin = new Padding(8, 0, 8, 0),
        Text = "● Service: —",
        ForeColor = SystemColors.GrayText,
        Spring = false,
    };

    private readonly ToolStripStatusLabel _pathStatusLabel = new()
    {
        Spring = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Text = "Open appsettings.json to begin.",
    };

    private readonly System.Windows.Forms.Timer _servicePollTimer;

    private bool _dirty;
    private bool _suppressDirty;

    public MainForm()
    {
        Text = "Synthetic HTTP Monitor — configuration";
        Width = 960;
        Height = 720;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 560);

        _servicePollTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _servicePollTimer.Tick += (_, _) => RefreshServiceStatus();

        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add("&Open appsettings.json…", null, (_, _) => OpenWithDialog());
        file.DropDownItems.Add("&Open installed copy", null, (_, _) => OpenInstalled());
        file.DropDownItems.Add("&Save changes\tCtrl+S", null, (_, _) => SaveCurrent());
        file.DropDownItems.Add("Save &as…", null, (_, _) => SaveAs());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("E&xit", null, (_, _) => Close());
        menu.Items.Add(file);
        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add("&About", null, (_, _) =>
        {
            var exe = Application.ExecutablePath;
            var ver = "?";
            try
            {
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    var vi = FileVersionInfo.GetVersionInfo(exe);
                    ver = string.IsNullOrWhiteSpace(vi.ProductVersion)
                        ? (vi.FileVersion?.Trim() ?? "?")
                        : vi.ProductVersion.Trim();
                }
            }
            catch
            {
                // ignore
            }

            MessageBox.Show(
                this,
                $"Synthetic HTTP Monitor — configuration editor\n\nVersion: {ver}\n\nEdits appsettings.json next to the monitor service. Use Save changes (toolbar or File menu) or Ctrl+S when you are done; other JSON files (Serilog, split targets.json, etc.) are left unchanged when you save.\n\nIf Windows blocks saving (for example under Program Files), the app will prompt for UAC elevation to write the file and restart the service when you are editing the installed copy.\n\nThe status bar shows whether the Synthetic HTTP Monitor Windows service is running.\n\nSee the repo README for how the build version (FileVersion) is stamped.",
                "About",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        });
        menu.Items.Add(help);
        MainMenuStrip = menu;

        var toolStrip = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(6, 2, 6, 2),
        };
        var saveChangesButton = new ToolStripButton("Save changes")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Font = new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Bold),
        };
        saveChangesButton.Click += (_, _) => SaveCurrent();
        var saveAsButton = new ToolStripButton("Save as…") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        saveAsButton.Click += (_, _) => SaveAs();
        toolStrip.Items.Add(saveChangesButton);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(saveAsButton);

        var tabs = new TabControl { Dock = DockStyle.Fill };

        var tabChecks = new TabPage("Monitored URLs");
        tabChecks.Padding = new Padding(8);
        var checksLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        checksLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        checksLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var defaultsBox = new GroupBox
        {
            Text = "Default values (used when a row leaves a cell blank)",
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8),
        };
        var defaultsTable = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 4,
            Padding = new Padding(0, 2, 0, 4),
        };
        defaultsTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        defaultsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        defaultsTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        defaultsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        var defRow = 0;
        defaultsTable.Controls.Add(new Label { Text = "Interval (seconds)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, defRow);
        defaultsTable.Controls.Add(_defInterval, 1, defRow);
        defaultsTable.Controls.Add(new Label { Text = "Timeout (seconds)", AutoSize = true, Anchor = AnchorStyles.Left }, 2, defRow);
        defaultsTable.Controls.Add(_defTimeout, 3, defRow);
        defRow++;
        defaultsTable.Controls.Add(new Label { Text = "OK status codes", AutoSize = true, Anchor = AnchorStyles.Left }, 0, defRow);
        _defCodes.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        defaultsTable.Controls.Add(_defCodes, 1, defRow);
        defaultsTable.SetColumnSpan(_defCodes, 3);
        defRow++;
        defaultsTable.Controls.Add(new Label { Text = "Max response body (bytes)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, defRow);
        defaultsTable.Controls.Add(_defMaxBody, 1, defRow);
        defaultsTable.SetColumnSpan(_defMaxBody, 3);
        defRow++;
        var httpsHelp = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(820, 0),
            ForeColor = SystemColors.GrayText,
            Text =
                "HTTPS: when a row leaves \"Skip HTTPS cert\" unset (mixed), the default below applies. Understand the MITM tradeoff before enabling.",
        };
        defaultsTable.Controls.Add(httpsHelp, 0, defRow);
        defaultsTable.SetColumnSpan(httpsHelp, 4);
        defRow++;
        _defSkipCert.Margin = new Padding(0, 2, 0, 0);
        defaultsTable.Controls.Add(_defSkipCert, 0, defRow);
        defaultsTable.SetColumnSpan(_defSkipCert, 4);
        defaultsBox.Controls.Add(defaultsTable);

        var gridBox = new GroupBox { Text = "Endpoints to monitor", Dock = DockStyle.Fill, Padding = new Padding(8) };
        var gridPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        gridPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        gridPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var btnRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Dock = DockStyle.Fill };
        var addBtn = new Button { Text = "Add row", AutoSize = true };
        addBtn.Click += (_, _) =>
        {
            AddEmptyTargetRow();
            MarkDirty();
        };
        var removeBtn = new Button { Text = "Remove selected", AutoSize = true };
        removeBtn.Click += (_, _) =>
        {
            foreach (DataGridViewRow r in _targetsGrid.SelectedRows)
            {
                if (!r.IsNewRow)
                {
                    _targetsGrid.Rows.Remove(r);
                }
            }

            MarkDirty();
        };
        btnRow.Controls.Add(addBtn);
        btnRow.Controls.Add(removeBtn);
        gridPanel.Controls.Add(btnRow, 0, 0);
        gridPanel.Controls.Add(_targetsGrid, 0, 1);
        gridBox.Controls.Add(gridPanel);

        BuildTargetColumns();

        checksLayout.Controls.Add(defaultsBox, 0, 0);
        checksLayout.Controls.Add(gridBox, 0, 1);
        tabChecks.Controls.Add(checksLayout);

        var tabSettings = new TabPage("Notifications");
        tabSettings.Padding = new Padding(8);
        var settingsScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var settingsStack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
            Dock = DockStyle.Top,
            Padding = new Padding(4),
        };

        settingsStack.Controls.Add(MakeGroup("When to alert", BuildAlertingPanel()));
        settingsStack.Controls.Add(MakeGroup("SMTP (outbound alerts)", BuildSmtpPanel()));
        settingsStack.Controls.Add(MakeGroup("PagerDuty (Events API v2)", BuildPagerDutyPanel()));
        settingsStack.Controls.Add(MakeGroup("Ticketing API (optional)", BuildTicketingPanel()));

        settingsScroll.Controls.Add(settingsStack);
        tabSettings.Controls.Add(settingsScroll);

        tabs.TabPages.Add(tabChecks);
        tabs.TabPages.Add(tabSettings);

        var status = new StatusStrip { ShowItemToolTips = true };
        _serviceStatusLabel.ToolTipText = "Synthetic HTTP Monitor Windows service";
        status.Items.Add(_serviceStatusLabel);
        status.Items.Add(_pathStatusLabel);

        var rootPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
        };
        rootPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootPanel.Controls.Add(menu, 0, 0);
        rootPanel.Controls.Add(toolStrip, 0, 1);
        rootPanel.Controls.Add(tabs, 0, 2);
        rootPanel.Controls.Add(status, 0, 3);
        Controls.Add(rootPanel);

        AttachDirtyHandlers(tabChecks);
        AttachDirtyHandlers(tabSettings);

        FormClosing += MainForm_FormClosing;

        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                e.SuppressKeyPress = true;
                SaveCurrent();
            }
        };

        Shown += (_, _) =>
        {
            RefreshServiceStatus();
            _servicePollTimer.Start();
            var dir = InstallPathResolver.TryGetInstallDirectoryFromService();
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }

            var installed = Path.Combine(dir, "appsettings.json");
            if (File.Exists(installed))
            {
                TryOpenPath(installed);
            }
        };

        void OpenWithDialog()
        {
            using var d = new OpenFileDialog
            {
                Filter = "appsettings.json|appsettings.json|JSON files|*.json|All files|*.*",
                FileName = "appsettings.json",
            };
            if (d.ShowDialog(this) == DialogResult.OK)
            {
                TryOpenPath(d.FileName);
            }
        }

        void OpenInstalled()
        {
            var dir = InstallPathResolver.TryGetInstallDirectoryFromService();
            if (string.IsNullOrEmpty(dir))
            {
                MessageBox.Show(
                    "The SyntheticHttpMonitor Windows service was not found on this machine (or its path could not be read). Use File → Open and pick appsettings.json from the install folder.",
                    "Not found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var path = Path.Combine(dir, "appsettings.json");
            if (!File.Exists(path))
            {
                MessageBox.Show($"No appsettings.json next to the service:\n{path}", "Not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            TryOpenPath(path);
        }

        void SaveCurrent()
        {
            if (string.IsNullOrEmpty(_currentPath))
            {
                SaveAs();
                return;
            }

            SaveToPath(_currentPath);
        }

        void SaveAs()
        {
            using var d = new SaveFileDialog
            {
                Filter = "JSON|*.json|All files|*.*",
                FileName = "appsettings.json",
            };
            if (d.ShowDialog(this) == DialogResult.OK)
            {
                if (SaveToPath(d.FileName))
                {
                    _currentPath = d.FileName;
                    UpdateTitle();
                }
            }
        }

        UpdateTitle();
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && _dirty)
        {
            var dr = MessageBox.Show(
                this,
                "You have unsaved changes. Save before closing?",
                "Unsaved changes",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);
            if (dr == DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (dr == DialogResult.Yes)
            {
                if (string.IsNullOrEmpty(_currentPath))
                {
                    using var d = new SaveFileDialog
                    {
                        Filter = "JSON|*.json|All files|*.*",
                        FileName = "appsettings.json",
                    };
                    if (d.ShowDialog(this) != DialogResult.OK)
                    {
                        e.Cancel = true;
                        return;
                    }

                    if (!SaveToPath(d.FileName))
                    {
                        e.Cancel = true;
                        return;
                    }

                    _currentPath = d.FileName;
                    UpdateTitle();
                }
                else
                {
                    if (!SaveToPath(_currentPath))
                    {
                        e.Cancel = true;
                        return;
                    }
                }
            }
        }

        if (!e.Cancel)
        {
            _servicePollTimer.Stop();
            _servicePollTimer.Dispose();
        }
    }

    private void AttachDirtyHandlers(Control root)
    {
        foreach (Control c in root.Controls)
        {
            AttachDirtyHandlersRecursive(c, MarkDirty);
        }
    }

    private static void AttachDirtyHandlersRecursive(Control c, Action markDirty)
    {
        switch (c)
        {
            case NumericUpDown n:
                n.ValueChanged += (_, _) => markDirty();
                return;
            case TextBox t:
                t.TextChanged += (_, _) => markDirty();
                return;
            case CheckBox cb:
                cb.CheckedChanged += (_, _) => markDirty();
                return;
            case DataGridView dg:
                dg.CellValueChanged += (_, _) => markDirty();
                return;
            case TabControl tc:
                foreach (TabPage p in tc.TabPages)
                {
                    foreach (Control ch in p.Controls)
                    {
                        AttachDirtyHandlersRecursive(ch, markDirty);
                    }
                }

                return;
            default:
                foreach (Control ch in c.Controls)
                {
                    AttachDirtyHandlersRecursive(ch, markDirty);
                }

                break;
        }
    }

    private void MarkDirty()
    {
        if (_suppressDirty)
        {
            return;
        }

        _dirty = true;
        UpdateTitle();
    }

    private void ClearDirty()
    {
        _dirty = false;
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        var baseTitle = _currentPath is null
            ? "Synthetic HTTP Monitor — configuration"
            : $"Synthetic HTTP Monitor — {_currentPath}";
        Text = _dirty ? $"{baseTitle} *" : baseTitle;
    }

    private static Control MakeGroup(string title, Control inner)
    {
        var g = new GroupBox
        {
            Text = title,
            AutoSize = true,
            Padding = new Padding(10),
            Margin = new Padding(0, 0, 0, 12),
        };
        inner.Dock = DockStyle.Fill;
        g.Controls.Add(inner);
        g.Width = 880;
        return g;
    }

    private Control BuildAlertingPanel()
    {
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(4) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.Controls.Add(new Label { Text = "Failures before first alert", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        t.Controls.Add(_alertFailThreshold, 1, 0);
        t.Controls.Add(new Label { Text = "Repeat alert while still down (minutes, 0 = only once)", AutoSize = true }, 0, 1);
        t.Controls.Add(_alertRepeatMin, 1, 1);
        t.Controls.Add(_alertRecovery, 1, 2);
        t.SetColumnSpan(_alertRecovery, 2);
        return t;
    }

    private Control BuildSmtpPanel()
    {
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(4) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var row = 0;
        void Row(string label, Control c)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            t.Controls.Add(c, 1, row);
            row++;
        }

        t.Controls.Add(_smtpEnabled, 1, row);
        t.SetColumnSpan(_smtpEnabled, 2);
        row++;
        Row("SMTP host", _smtpHost);
        Row("Port", _smtpPort);
        t.Controls.Add(_smtpSsl, 1, row);
        t.SetColumnSpan(_smtpSsl, 2);
        row++;
        t.Controls.Add(_smtpStartTls, 1, row);
        t.SetColumnSpan(_smtpStartTls, 2);
        row++;
        Row("Username (optional)", _smtpUser);
        Row("Password (optional)", _smtpPass);
        Row("From address", _smtpFrom);
        Row("To (comma-separated)", _smtpTo);
        Row("Cc (optional)", _smtpCc);
        Row("Subject prefix when DOWN", _smtpSubDown);
        Row("Subject prefix when recovered", _smtpSubRec);
        return t;
    }

    private Control BuildTicketingPanel()
    {
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(4) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var row = 0;
        t.Controls.Add(_tickEnabled, 1, row);
        t.SetColumnSpan(_tickEnabled, 2);
        row++;
        void Row(string label, Control c)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true }, 0, row);
            t.Controls.Add(c, 1, row);
            row++;
        }

        Row("Base URL", _tickBaseUrl);
        Row("API key header name", _tickHeader);
        Row("API key", _tickApiKey);
        Row("Project key (optional)", _tickProject);
        return t;
    }

    private Control BuildPagerDutyPanel()
    {
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(4) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var row = 0;
        t.Controls.Add(_pdEnabled, 1, row);
        t.SetColumnSpan(_pdEnabled, 2);
        row++;
        void Row(string label, Control c)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            t.Controls.Add(c, 1, row);
            row++;
        }

        Row("Routing key (Events integration)", _pdRoutingKey);
        Row("Events API URL", _pdEventsUrl);
        Row("Severity (critical, error, warning, info)", _pdSeverity);
        Row("Source label (optional)", _pdSource);
        t.Controls.Add(_pdResolveOnRecovery, 1, row);
        t.SetColumnSpan(_pdResolveOnRecovery, 2);
        row++;
        t.Controls.Add(
            new Label
            {
                AutoSize = true,
                MaximumSize = new Size(820, 0),
                ForeColor = SystemColors.GrayText,
                Text =
                    "Summary text includes the target name, URL, and failure reason. Uses POST https://events.pagerduty.com/v2/enqueue unless you override the URL.",
            },
            1,
            row);
        t.SetColumnSpan(t.Controls[^1], 2);
        return t;
    }

    private void BuildTargetColumns()
    {
        _targetsGrid.Columns.Clear();
        _targetsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

        void AddTextColumn(string name, string header, int width, int minW = 40)
        {
            var col = new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                Width = width,
                MinimumWidth = minW,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            };
            _targetsGrid.Columns.Add(col);
        }

        AddTextColumn("Name", "Friendly name", 130, 80);
        AddTextColumn("Url", "URL (https://…)", 280, 160);
        var skipCol = new DataGridViewCheckBoxColumn
        {
            Name = "SkipHttpsCert",
            HeaderText = "Skip HTTPS cert",
            ThreeState = true,
            Width = 110,
            MinimumWidth = 90,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        _targetsGrid.Columns.Add(skipCol);
        AddTextColumn("Interval", "Interval sec (optional)", 95, 70);
        AddTextColumn("Timeout", "Timeout sec (optional)", 95, 70);
        AddTextColumn("Codes", "OK status codes (optional)", 140, 90);
        AddTextColumn("BodyRegex", "Body must match (regex, optional)", 200, 120);
        AddTextColumn("MaxBody", "Max body bytes (optional)", 120, 90);
    }

    private void AddEmptyTargetRow() =>
        _targetsGrid.Rows.Add("", "", DBNull.Value, "", "", "", "", "");

    private void TryOpenPath(string path)
    {
        _suppressDirty = true;
        try
        {
            var json = File.ReadAllText(path);
            _root = AppsettingsJson.ParseRoot(json);
            var syn = AppsettingsJson.ReadSyntheticMonitor(_root);
            var alert = AppsettingsJson.ReadAlerting(_root);
            var smtp = AppsettingsJson.ReadSmtp(_root);
            var tick = AppsettingsJson.ReadTicketing(_root);
            var pd = AppsettingsJson.ReadPagerDuty(_root);

            _defInterval.Value = Math.Clamp(syn.Defaults.IntervalSeconds, (int)_defInterval.Minimum, (int)_defInterval.Maximum);
            _defTimeout.Value = Math.Clamp(syn.Defaults.TimeoutSeconds, (int)_defTimeout.Minimum, (int)_defTimeout.Maximum);
            _defCodes.Text = string.Join(", ", syn.Defaults.ExpectedStatusCodes);
            _defMaxBody.Value = Math.Clamp(syn.Defaults.MaxBodyBytes, (int)_defMaxBody.Minimum, (int)_defMaxBody.Maximum);
            _defSkipCert.Checked = syn.Defaults.DangerousAcceptAnyServerCertificate;

            _targetsGrid.Rows.Clear();
            foreach (var t in syn.Targets)
            {
                object skipCell = t.DangerousAcceptAnyServerCertificate switch
                {
                    true => true,
                    false => false,
                    null => DBNull.Value,
                };
                _targetsGrid.Rows.Add(
                    t.Name,
                    t.Url,
                    skipCell,
                    t.IntervalSeconds?.ToString() ?? "",
                    t.TimeoutSeconds?.ToString() ?? "",
                    t.ExpectedStatusCodes is { Count: > 0 } ? string.Join(", ", t.ExpectedStatusCodes) : "",
                    t.BodyRegex ?? "",
                    t.MaxBodyBytes?.ToString() ?? "");
            }

            if (_targetsGrid.Rows.Count == 0)
            {
                AddEmptyTargetRow();
            }

            _alertFailThreshold.Value = Math.Clamp(alert.FailureThreshold, (int)_alertFailThreshold.Minimum, (int)_alertFailThreshold.Maximum);
            _alertRepeatMin.Value = Math.Clamp(alert.RepeatWhileDownMinutes, (int)_alertRepeatMin.Minimum, (int)_alertRepeatMin.Maximum);
            _alertRecovery.Checked = alert.SendRecoveryEmail;

            _smtpEnabled.Checked = smtp.Enabled;
            _smtpHost.Text = smtp.Host;
            _smtpPort.Value = Math.Clamp(smtp.Port, (int)_smtpPort.Minimum, (int)_smtpPort.Maximum);
            _smtpSsl.Checked = smtp.UseSsl;
            _smtpStartTls.Checked = smtp.UseStartTls;
            _smtpUser.Text = smtp.Username ?? "";
            _smtpPass.Text = smtp.Password ?? "";
            _smtpFrom.Text = smtp.From;
            _smtpTo.Text = string.Join(", ", smtp.To);
            _smtpCc.Text = string.Join(", ", smtp.Cc);
            _smtpSubDown.Text = smtp.SubjectDownPrefix;
            _smtpSubRec.Text = smtp.SubjectRecoveryPrefix;

            _tickEnabled.Checked = tick.Enabled;
            _tickBaseUrl.Text = tick.BaseUrl;
            _tickHeader.Text = tick.ApiKeyHeaderName;
            _tickApiKey.Text = tick.ApiKey;
            _tickProject.Text = tick.ProjectKey ?? "";

            _pdEnabled.Checked = pd.Enabled;
            _pdRoutingKey.Text = pd.RoutingKey ?? "";
            _pdEventsUrl.Text = string.IsNullOrWhiteSpace(pd.EventsApiUrl)
                ? "https://events.pagerduty.com/v2/enqueue"
                : pd.EventsApiUrl;
            _pdSeverity.Text = string.IsNullOrWhiteSpace(pd.Severity) ? "critical" : pd.Severity;
            _pdSource.Text = pd.Source ?? "";
            _pdResolveOnRecovery.Checked = pd.ResolveOnRecovery;

            _currentPath = path;
            _pathStatusLabel.Text = path;
            RefreshServiceStatus();
            ClearDirty();
            UpdateTitle();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open file", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    private void RefreshServiceStatus()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        var state = MonitorConfigRuntimeHelper.GetMonitorServiceState();
        switch (state)
        {
            case MonitorConfigRuntimeHelper.MonitorServiceState.Running:
                _serviceStatusLabel.Text = "● Service: Running";
                _serviceStatusLabel.ForeColor = Color.ForestGreen;
                break;
            case MonitorConfigRuntimeHelper.MonitorServiceState.Stopped:
                _serviceStatusLabel.Text = "● Service: Stopped";
                _serviceStatusLabel.ForeColor = Color.Firebrick;
                break;
            case MonitorConfigRuntimeHelper.MonitorServiceState.StartPending:
                _serviceStatusLabel.Text = "● Service: Starting…";
                _serviceStatusLabel.ForeColor = Color.DarkOrange;
                break;
            case MonitorConfigRuntimeHelper.MonitorServiceState.StopPending:
                _serviceStatusLabel.Text = "● Service: Stopping…";
                _serviceStatusLabel.ForeColor = Color.DarkOrange;
                break;
            case MonitorConfigRuntimeHelper.MonitorServiceState.NotInstalled:
                _serviceStatusLabel.Text = "● Service: Not installed";
                _serviceStatusLabel.ForeColor = SystemColors.GrayText;
                break;
            default:
                _serviceStatusLabel.Text = "● Service: Unknown";
                _serviceStatusLabel.ForeColor = SystemColors.GrayText;
                break;
        }
    }

    /// <summary>After a successful write to disk: restart the service when editing the live install appsettings.</summary>
    private ServiceRestartOutcome FinishSaveRestart(bool wantRestart)
    {
        if (!wantRestart)
        {
            RefreshServiceStatus();
            return ServiceRestartOutcome.NotRequested;
        }

        var (ok, msg) = MonitorConfigRuntimeHelper.TryRestartMonitorServiceInCurrentSession();
        if (ok)
        {
            RefreshServiceStatus();
            return ServiceRestartOutcome.RestartedInSession;
        }

        var (exit, cancelled) = MonitorConfigRuntimeHelper.TryRestartServiceElevated();
        if (cancelled)
        {
            MessageBox.Show(
                this,
                "The configuration file was saved successfully.\n\nYou cancelled the restart step at the User Account Control prompt.\n\nRestart the \"Synthetic HTTP Monitor\" service manually (services.msc) for changes to apply.",
                "Service not restarted",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            RefreshServiceStatus();
            return ServiceRestartOutcome.RestartCancelledByUser;
        }

        if (exit == 0)
        {
            RefreshServiceStatus();
            return ServiceRestartOutcome.RestartedElevated;
        }

        var detail = string.IsNullOrWhiteSpace(msg)
            ? "The elevated restart step did not complete successfully."
            : msg;
        MessageBox.Show(
            this,
            "The configuration file was saved successfully.\n\nThe service could not be restarted automatically:\n"
            + detail
            + "\n\nRestart the \"Synthetic HTTP Monitor\" service manually if needed.",
            "Service not restarted",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        RefreshServiceStatus();
        return ServiceRestartOutcome.RestartFailed;
    }

    private void ShowSaveSuccessFeedback(string path, bool wantRestart, ServiceRestartOutcome outcome)
    {
        if (!wantRestart)
        {
            MessageBox.Show(
                this,
                $"Configuration was saved successfully to:\n{path}\n\nRestart the Synthetic HTTP Monitor Windows service when you want these changes to take effect.",
                "Save successful",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        switch (outcome)
        {
            case ServiceRestartOutcome.RestartedInSession:
            case ServiceRestartOutcome.RestartedElevated:
                MessageBox.Show(
                    this,
                    "Configuration was saved successfully.\n\nThe Synthetic HTTP Monitor service was restarted. Your changes are now active.",
                    "Save successful",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            case ServiceRestartOutcome.RestartCancelledByUser:
            case ServiceRestartOutcome.RestartFailed:
                return;
            default:
                return;
        }
    }

    private bool SaveToPath(string path)
    {
        string json;
        try
        {
            var syn = ReadSyntheticFromUi();
            var alert = ReadAlertingFromUi();
            var smtp = ReadSmtpFromUi();
            var tick = ReadTicketingFromUi();
            var pd = ReadPagerDutyFromUi();

            AppsettingsJson.ApplySections(_root, syn, alert, smtp, tick, pd);
            json = AppsettingsJson.SerializeRoot(_root);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        var wantRestart = MonitorConfigRuntimeHelper.IsSavedPathActiveInstallAppSettings(path);

        try
        {
            File.WriteAllText(path, json);
            _pathStatusLabel.Text = path;
            var outcome = FinishSaveRestart(wantRestart);
            ShowSaveSuccessFeedback(path, wantRestart, outcome);
            ClearDirty();
            return true;
        }
        catch (Exception ex) when (MonitorConfigRuntimeHelper.IsFileAccessDenied(ex))
        {
            var temp = Path.Combine(Path.GetTempPath(), $"shm-appsettings-{Guid.NewGuid():n}.json");
            try
            {
                File.WriteAllText(temp, json);
            }
            catch (Exception tex)
            {
                MessageBox.Show(
                    this,
                    $"Could not write a temporary file before elevation:\n{tex.Message}",
                    "Could not save",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }

            var (exit, cancelled) = MonitorConfigRuntimeHelper.TrySaveViaElevationAndRestart(temp, path, wantRestart);
            if (cancelled)
            {
                TryDeleteFileIgnoreErrors(temp);
                MessageBox.Show(
                    this,
                    "Save was cancelled at the User Account Control prompt.",
                    "Could not save",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return false;
            }

            if (exit == 1)
            {
                TryDeleteFileIgnoreErrors(temp);
                MessageBox.Show(
                    this,
                    "Administrator mode could not copy the configuration file to the destination.",
                    "Could not save",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }

            _pathStatusLabel.Text = path;
            if (exit == 2 && wantRestart)
            {
                MessageBox.Show(
                    this,
                    "The configuration file was saved successfully.\n\nThe Synthetic HTTP Monitor service could not be restarted from the elevated session.\n\nRestart it from Services (services.msc) or an elevated console.",
                    "Service not restarted",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            else
            {
                ShowSaveSuccessFeedback(
                    path,
                    wantRestart,
                    wantRestart ? ServiceRestartOutcome.RestartedElevated : ServiceRestartOutcome.NotRequested);
            }

            RefreshServiceStatus();
            ClearDirty();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                FormatSaveFailureMessage(ex, path),
                "Could not save",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
    }

    private static void TryDeleteFileIgnoreErrors(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string FormatSaveFailureMessage(Exception ex, string path)
    {
        if (!MonitorConfigRuntimeHelper.IsFileAccessDenied(ex))
        {
            return ex.Message;
        }

        var sb = new System.Text.StringBuilder(ex.Message);
        if (IsUnderProgramFiles(path))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine(
                "Installed apps under Program Files are usually not writable by normal users. When you use Save changes, the app will try a UAC elevation prompt to write the file (and restart the service if you are editing the installed copy).");
            sb.AppendLine("You can also run this app as Administrator once, or use Save as… to write a copy elsewhere.");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Check that you have write permission to this folder (and that the file is not read-only).");
        }

        return sb.ToString();
    }

    private static bool IsUnderProgramFiles(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (pf.Length > 0)
            {
                if (full.Equals(pf, StringComparison.OrdinalIgnoreCase)
                    || full.StartsWith(pf.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (pfx86.Length > 0)
            {
                if (full.Equals(pfx86, StringComparison.OrdinalIgnoreCase)
                    || full.StartsWith(pfx86.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private SyntheticMonitorOptions ReadSyntheticFromUi()
    {
        var preservedHttp = AppsettingsJson.ReadSyntheticMonitor(_root).HttpClient;

        var defaults = new TargetDefaults
        {
            IntervalSeconds = (int)_defInterval.Value,
            TimeoutSeconds = (int)_defTimeout.Value,
            ExpectedStatusCodes = ParseCodes(_defCodes.Text) ?? [200],
            MaxBodyBytes = (int)_defMaxBody.Value,
            DangerousAcceptAnyServerCertificate = _defSkipCert.Checked,
        };

        var targets = new List<TargetOptions>();
        foreach (DataGridViewRow row in _targetsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            string Cell(int i) => row.Cells[i].Value?.ToString()?.Trim() ?? "";
            var name = Cell(0);
            var url = Cell(1);
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var t = new TargetOptions
            {
                Name = name,
                Url = url,
                DangerousAcceptAnyServerCertificate = ReadTlsSkipOverride(row),
                IntervalSeconds = ParseOptionalInt(Cell(3)),
                TimeoutSeconds = ParseOptionalInt(Cell(4)),
                ExpectedStatusCodes = ParseCodes(Cell(5)),
                BodyRegex = string.IsNullOrWhiteSpace(Cell(6)) ? null : Cell(6),
                MaxBodyBytes = ParseOptionalInt(Cell(7)),
            };
            targets.Add(t);
        }

        return new SyntheticMonitorOptions
        {
            Defaults = defaults,
            HttpClient = new HttpClientTlsOptions
            {
                DangerousAcceptAnyServerCertificate = preservedHttp.DangerousAcceptAnyServerCertificate,
            },
            Targets = targets,
        };
    }

    private static bool? ReadTlsSkipOverride(DataGridViewRow row)
    {
        if (row.DataGridView is null || !row.DataGridView.Columns.Contains("SkipHttpsCert"))
        {
            return null;
        }

        return row.Cells["SkipHttpsCert"].Value switch
        {
            true => true,
            false => false,
            _ => null,
        };
    }

    private AlertingOptions ReadAlertingFromUi() =>
        new()
        {
            FailureThreshold = (int)_alertFailThreshold.Value,
            RepeatWhileDownMinutes = (int)_alertRepeatMin.Value,
            SendRecoveryEmail = _alertRecovery.Checked,
        };

    private SmtpOptions ReadSmtpFromUi() =>
        new()
        {
            Enabled = _smtpEnabled.Checked,
            Host = _smtpHost.Text.Trim(),
            Port = (int)_smtpPort.Value,
            UseSsl = _smtpSsl.Checked,
            UseStartTls = _smtpStartTls.Checked,
            Username = string.IsNullOrWhiteSpace(_smtpUser.Text) ? null : _smtpUser.Text.Trim(),
            Password = string.IsNullOrWhiteSpace(_smtpPass.Text) ? null : _smtpPass.Text,
            From = _smtpFrom.Text.Trim(),
            To = SplitEmails(_smtpTo.Text),
            Cc = SplitEmails(_smtpCc.Text),
            SubjectDownPrefix = string.IsNullOrWhiteSpace(_smtpSubDown.Text) ? "[DOWN]" : _smtpSubDown.Text.Trim(),
            SubjectRecoveryPrefix = string.IsNullOrWhiteSpace(_smtpSubRec.Text) ? "[RECOVERED]" : _smtpSubRec.Text.Trim(),
        };

    private TicketingApiOptions ReadTicketingFromUi() =>
        new()
        {
            Enabled = _tickEnabled.Checked,
            BaseUrl = _tickBaseUrl.Text.Trim(),
            ApiKeyHeaderName = string.IsNullOrWhiteSpace(_tickHeader.Text) ? "X-API-Key" : _tickHeader.Text.Trim(),
            ApiKey = _tickApiKey.Text,
            ProjectKey = string.IsNullOrWhiteSpace(_tickProject.Text) ? null : _tickProject.Text.Trim(),
        };

    private PagerDutyOptions ReadPagerDutyFromUi() =>
        new()
        {
            Enabled = _pdEnabled.Checked,
            RoutingKey = _pdRoutingKey.Text.Trim(),
            EventsApiUrl = string.IsNullOrWhiteSpace(_pdEventsUrl.Text)
                ? "https://events.pagerduty.com/v2/enqueue"
                : _pdEventsUrl.Text.Trim(),
            Severity = string.IsNullOrWhiteSpace(_pdSeverity.Text) ? "critical" : _pdSeverity.Text.Trim(),
            Source = string.IsNullOrWhiteSpace(_pdSource.Text) ? string.Empty : _pdSource.Text.Trim(),
            ResolveOnRecovery = _pdResolveOnRecovery.Checked,
        };

    private static List<string> SplitEmails(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .ToList();

    private static int? ParseOptionalInt(string s) =>
        int.TryParse(s, out var v) ? v : null;

    private static List<int>? ParseCodes(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<int>();
        foreach (var p in parts)
        {
            if (int.TryParse(p, out var n))
            {
                list.Add(n);
            }
        }

        return list.Count > 0 ? list : null;
    }
}
