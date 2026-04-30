using System.Windows.Forms;

namespace SyntheticHttpMonitor.Setup;

internal sealed class SetupForm : Form
{
    private const string DefaultServiceName = "SyntheticHttpMonitor";
    private const string DefaultDisplayName = "Synthetic HTTP Monitor";

    private readonly TextBox _installPath = new() { Width = 520 };
    private readonly TextBox _serviceName = new() { Width = 200 };
    private readonly TextBox _displayName = new() { Width = 320 };
    private readonly CheckBox _startService = new() { AutoSize = true, Text = "Start the service when finished" };
    private readonly Label _versionBanner = new()
    {
        AutoSize = true,
        MaximumSize = new Size(600, 0),
        ForeColor = SystemColors.GrayText,
        Padding = new Padding(0, 4, 0, 0),
    };

    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = false,
        Font = new Font("Consolas", 9f),
        Height = 200,
        Dock = DockStyle.Fill,
    };

    private readonly Button _btnInstall = new() { AutoSize = true, Text = "Install" };
    private readonly Button _btnUninstall = new() { AutoSize = true, Text = "Uninstall" };

    public SetupForm()
    {
        Text = "Synthetic HTTP Monitor — setup";
        Width = 640;
        Height = 540;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(560, 460);

        _installPath.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SyntheticHttpMonitor");
        _serviceName.Text = DefaultServiceName;
        _displayName.Text = DefaultDisplayName;
        _serviceName.TextChanged += (_, _) => RefreshInstallUi();

        _btnInstall.Click += async (_, _) => await RunInstallOrUpgradeAsync();
        _btnUninstall.Click += (_, _) => RunUninstall();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 5,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(600, 0),
            Text = "Run this program from the unzipped release folder (same level as readme.md). Administrator rights are required.",
        };

        var fields = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 8, 0, 0) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var row = 0;
        void AddRow(string labelText, Control c)
        {
            fields.Controls.Add(new Label { Text = labelText, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            fields.Controls.Add(c, 1, row);
            row++;
        }

        AddRow("Install folder", _installPath);
        AddRow("Service name (internal)", _serviceName);
        AddRow("Display name", _displayName);
        fields.Controls.Add(_startService, 1, row);
        fields.SetColumnSpan(_startService, 2);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Padding = new Padding(0, 12, 0, 0) };
        buttons.Controls.Add(_btnInstall);
        buttons.Controls.Add(_btnUninstall);

        root.Controls.Add(intro, 0, 0);
        root.Controls.Add(_versionBanner, 0, 1);
        root.Controls.Add(fields, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        root.Controls.Add(_log, 0, 4);
        Controls.Add(root);

        RefreshInstallUi();
    }

    private static string PackageMonitorExePath(string setupBaseDirectory) =>
        Path.Combine(InstallOperations.GetPackagePayloadDirectory(setupBaseDirectory), InstallOperations.ExeName);

    private static string InstalledMonitorExePath(string installRoot)
    {
        var expected = InstallOperations.GetExpectedServiceExePath(installRoot);
        return File.Exists(expected) ? expected : Path.Combine(installRoot, InstallOperations.ExeName);
    }

    private static string UninstallCommandFor(string installRoot)
    {
        var installer = Path.Combine(installRoot, InstallOperations.InstallerExeName);
        if (File.Exists(installer))
        {
            return $"\"{installer}\" /uninstall";
        }

        var legacy = Path.Combine(installRoot, InstallOperations.LegacySetupExeName);
        if (File.Exists(legacy))
        {
            return $"\"{legacy}\" /uninstall";
        }

        return $"\"{Application.ExecutablePath}\" /uninstall";
    }

    private void RefreshInstallUi()
    {
        var sourceDir = AppContext.BaseDirectory;
        var packageExe = PackageMonitorExePath(sourceDir);
        var packageVer = File.Exists(packageExe)
            ? MonitorExeVersion.Read(packageExe)
            : "(SyntheticHttpMonitor.exe not found — use the release zip layout: readme + Installer at top level, binaries under Resources.)";

        var serviceName = _serviceName.Text.Trim();
        if (string.IsNullOrEmpty(serviceName))
        {
            _versionBanner.Text = $"Package in this folder: {packageVer}. Enter a service name.";
            _btnInstall.Text = "Install";
            return;
        }

        var serviceBinDir = ServicePathHelper.GetServiceBinaryDirectory(serviceName);
        if (!string.IsNullOrEmpty(serviceBinDir))
        {
            var installRoot = InstallOperations.GetInstallRootFromServiceBinaryDirectory(serviceBinDir);
            _installPath.Text = installRoot;
            _btnInstall.Text = "Upgrade (replace files)";
            var installedExe = InstalledMonitorExePath(installRoot);
            var installedVer = File.Exists(installedExe)
                ? MonitorExeVersion.Read(installedExe)
                : "(installed service binary not found)";
            _versionBanner.Text =
                $"Package in this folder: {packageVer}  →  Installed now: {installedVer}. Install folder files will be overwritten (no uninstall needed).";
        }
        else
        {
            _btnInstall.Text = "Install";
            _versionBanner.Text =
                $"Package in this folder: {packageVer}. No Windows service named '{serviceName}' — this will be a new install.";
        }
    }

    private void Log(string line)
    {
        _log.AppendText(line + Environment.NewLine);
    }

    private async Task RunInstallOrUpgradeAsync()
    {
        _btnInstall.Enabled = false;
        _btnUninstall.Enabled = false;
        _log.Clear();

        try
        {
            var serviceName = _serviceName.Text.Trim();
            var displayName = _displayName.Text.Trim();
            if (string.IsNullOrEmpty(serviceName) || string.IsNullOrEmpty(displayName))
            {
                throw new InvalidOperationException("Service name and display name are required.");
            }

            var sourceDir = AppContext.BaseDirectory;
            var mainExe = PackageMonitorExePath(sourceDir);
            if (!File.Exists(mainExe))
            {
                throw new FileNotFoundException(
                    $"SyntheticHttpMonitor.exe was not found in this package (looked under Resources and next to the installer):\n{mainExe}");
            }

            var packageVer = MonitorExeVersion.Read(mainExe);
            Log($"Package version (this folder): {packageVer}");

            var existingServiceBinDir = ServicePathHelper.GetServiceBinaryDirectory(serviceName);
            var upgrade = !string.IsNullOrEmpty(existingServiceBinDir);
            var installRoot = upgrade
                ? InstallOperations.GetInstallRootFromServiceBinaryDirectory(existingServiceBinDir!)
                : _installPath.Text.Trim();

            if (string.IsNullOrEmpty(installRoot))
            {
                throw new InvalidOperationException("Choose an install folder.");
            }

            if (upgrade)
            {
                var before = MonitorExeVersion.Read(InstalledMonitorExePath(installRoot));
                Log($"Installed version (before replace): {before}");
                Log($"Upgrading into: {installRoot}");
            }
            else
            {
                Log($"Installing into: {installRoot}");
            }

            var wasRunning = upgrade && InstallOperations.IsServiceRunning(serviceName);

            await Task.Run(() =>
            {
                if (upgrade)
                {
                    InstallOperations.StopServiceIfRunning(serviceName);
                }

                InstallOperations.CopyPayload(sourceDir, installRoot, upgrade: upgrade);
                InstallOperations.CopyInstallerTo(installRoot, Application.ExecutablePath);

                if (!upgrade)
                {
                    InstallOperations.CreateWindowsService(serviceName, displayName, installRoot);
                }
                else
                {
                    InstallOperations.EnsureServiceBinaryPath(serviceName, installRoot);
                }

                var monitorExeForIcon = InstalledMonitorExePath(installRoot);
                InstallOperations.RegisterProgramsAndFeatures(
                    serviceName,
                    displayName,
                    publisher: "Synthetic HTTP Monitor",
                    installLocation: installRoot,
                    uninstallCommand: UninstallCommandFor(installRoot),
                    exePathForIcon: monitorExeForIcon);

                InstallOperations.ConfigureServiceRecovery(serviceName);

                if (_startService.Checked || wasRunning)
                {
                    InstallOperations.StartServiceIfExists(serviceName);
                }
            });

            var after = MonitorExeVersion.Read(InstalledMonitorExePath(installRoot));
            Log($"Installed version (after replace): {after}");
            Log("Done.");
            MessageBox.Show(
                _startService.Checked || wasRunning
                    ? "Finished. The service was updated or started as requested."
                    : "Finished. Set the service Log On account in services.msc if needed, then start the service.",
                "Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Setup failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnInstall.Enabled = true;
            _btnUninstall.Enabled = true;
            RefreshInstallUi();
        }
    }

    private void RunUninstall()
    {
        if (MessageBox.Show(
                "Remove the Windows service and the Programs and Features entry? Your install folder will not be deleted.",
                "Confirm uninstall",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var code = UninstallRunner.Run(silent: false, _serviceName.Text.Trim());
        if (code == 0)
        {
            RefreshInstallUi();
        }
    }
}
