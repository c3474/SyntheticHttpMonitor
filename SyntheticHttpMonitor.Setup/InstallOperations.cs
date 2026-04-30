using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;

namespace SyntheticHttpMonitor.Setup;

internal static class InstallOperations
{
    internal const string ResourcesFolderName = "Resources";
    internal const string ExeName = "SyntheticHttpMonitor.exe";
    internal const string InstallerExeName = "SyntheticHttpMonitor.Installer.exe";
    internal const string LegacySetupExeName = "SyntheticHttpMonitor.Setup.exe";
    private static readonly string[] SkipRootFiles = ["README.md", "readme.md"];

    /// <summary>Live JSON next to the service; not replaced from the package on upgrade when already present.</summary>
    private static readonly string[] OperatorJsonPreservedOnUpgrade =
    [
        "appsettings.json",
        "targets.json",
        "logging.json",
        "notifications.json",
    ];

    private static readonly string[] LegacyBundledRootConfigArtifacts =
    [
        "SyntheticHttpMonitor.Config.exe",
        "SyntheticHttpMonitor.Config.dll",
        "SyntheticHttpMonitor.Config.deps.json",
        "SyntheticHttpMonitor.Config.runtimeconfig.json",
        "SyntheticHttpMonitor.Config.pdb",
        "SyntheticHttpMonitor.Configuration.dll",
    ];

    /// <summary>Directory containing SyntheticHttpMonitor.exe in the package (Resources when zipped, or flat for dev builds).</summary>
    public static string GetPackagePayloadDirectory(string setupBaseDirectory)
    {
        var nested = Path.Combine(setupBaseDirectory, ResourcesFolderName, ExeName);
        if (File.Exists(nested))
        {
            return Path.Combine(setupBaseDirectory, ResourcesFolderName);
        }

        return setupBaseDirectory;
    }

    public static string GetExpectedServiceExePath(string installRoot) =>
        Path.Combine(installRoot, ResourcesFolderName, ExeName);

    /// <summary>Install root (e.g. Program Files\SyntheticHttpMonitor): parent of Resources if the service exe lives under Resources.</summary>
    public static string GetInstallRootFromServiceBinaryDirectory(string? serviceBinaryDirectory)
    {
        if (string.IsNullOrEmpty(serviceBinaryDirectory))
        {
            return string.Empty;
        }

        var name = Path.GetFileName(serviceBinaryDirectory.TrimEnd(Path.DirectorySeparatorChar));
        if (name.Equals(ResourcesFolderName, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(serviceBinaryDirectory) ?? serviceBinaryDirectory;
        }

        return serviceBinaryDirectory;
    }

    public static void CopyPayload(string sourceDir, string installRoot, bool upgrade)
    {
        Directory.CreateDirectory(installRoot);

        var payloadRoot = GetPackagePayloadDirectory(sourceDir);
        if (payloadRoot.Equals(sourceDir, StringComparison.OrdinalIgnoreCase))
        {
            CopyPayloadLegacyFlatSource(sourceDir, installRoot, upgrade);
        }
        else
        {
            CopyPayloadFromBundledResources(payloadRoot, installRoot, upgrade);
            RemoveObsoleteBundledRootConfigArtifacts(installRoot);
        }

        var logsDir = Path.Combine(installRoot, "logs");
        Directory.CreateDirectory(logsDir);
    }

    /// <summary>Earlier builds copied Config* assemblies to the install root without the shared runtime; remove those orphans on upgrade.</summary>
    private static void RemoveObsoleteBundledRootConfigArtifacts(string installRoot)
    {
        foreach (var name in LegacyBundledRootConfigArtifacts)
        {
            var path = Path.Combine(installRoot, name);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // ignore — file may be locked or permission denied
            }
        }
    }

    /// <summary>On upgrade, do not replace existing operator-edited JSON (same names as optional split config files).</summary>
    private static bool SkipCopyToPreserveOperatorJsonOnUpgrade(bool upgrade, string destinationPath)
    {
        if (!upgrade || !File.Exists(destinationPath))
        {
            return false;
        }

        var name = Path.GetFileName(destinationPath);
        foreach (var keep in OperatorJsonPreservedOnUpgrade)
        {
            if (name.Equals(keep, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void CopyPayloadLegacyFlatSource(string sourceDir, string installRoot, bool upgrade)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            if (IsSkippedRootFile(relative))
            {
                continue;
            }

            var dest = Path.Combine(installRoot, relative);
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            if (SkipCopyToPreserveOperatorJsonOnUpgrade(upgrade, dest))
            {
                continue;
            }

            File.Copy(file, dest, overwrite: true);
        }

        if (!upgrade)
        {
            var destAppsettings = Path.Combine(installRoot, "appsettings.json");
            var example = Path.Combine(installRoot, "appsettings.Example.json");
            if (!File.Exists(destAppsettings) && File.Exists(example))
            {
                File.Copy(example, destAppsettings, overwrite: false);
            }
        }
    }

    private static void CopyPayloadFromBundledResources(string payloadRoot, string installRoot, bool upgrade)
    {
        var resourcesDest = Path.Combine(installRoot, ResourcesFolderName);
        Directory.CreateDirectory(resourcesDest);

        foreach (var file in Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories))
        {
            var relativeFromPayload = Path.GetRelativePath(payloadRoot, file);
            var fileName = Path.GetFileName(file);
            if (fileName.Equals(LegacySetupExeName, StringComparison.OrdinalIgnoreCase)
                || fileName.Equals(InstallerExeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Keep example JSON at install root for visibility. Do not lift SyntheticHttpMonitor.Config*.exe
            // (or related assemblies) to the root — they must stay next to the shared self-contained runtime in Resources.
            if (fileName.EndsWith(".Example.json", StringComparison.OrdinalIgnoreCase))
            {
                var dest = Path.Combine(installRoot, fileName);
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                File.Copy(file, dest, overwrite: true);
                continue;
            }

            var destUnderResources = Path.Combine(resourcesDest, relativeFromPayload);
            var destDirR = Path.GetDirectoryName(destUnderResources);
            if (!string.IsNullOrEmpty(destDirR))
            {
                Directory.CreateDirectory(destDirR);
            }

            if (SkipCopyToPreserveOperatorJsonOnUpgrade(upgrade, destUnderResources))
            {
                continue;
            }

            File.Copy(file, destUnderResources, overwrite: true);
        }

        if (!upgrade)
        {
            var destAppsettings = Path.Combine(installRoot, ResourcesFolderName, "appsettings.json");
            var example = Path.Combine(installRoot, ResourcesFolderName, "appsettings.Example.json");
            if (!File.Exists(destAppsettings) && File.Exists(example))
            {
                File.Copy(example, destAppsettings, overwrite: false);
            }
        }
    }

    private static bool IsSkippedRootFile(string relativePath)
    {
        if (relativePath.Contains(Path.DirectorySeparatorChar) || relativePath.Contains('/'))
        {
            return false;
        }

        var name = Path.GetFileName(relativePath);
        return SkipRootFiles.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    public static void CopyInstallerTo(string installRoot, string runningInstallerPath)
    {
        var dest = Path.Combine(installRoot, InstallerExeName);
        File.Copy(runningInstallerPath, dest, overwrite: true);
    }

    public static void CreateWindowsService(string serviceName, string displayName, string installRoot)
    {
        var exePath = File.Exists(GetExpectedServiceExePath(installRoot))
            ? GetExpectedServiceExePath(installRoot)
            : Path.Combine(installRoot, ExeName);
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"Service executable not found: {exePath}");
        }

        RunSc($"create {serviceName} binPath= \"{exePath}\" DisplayName= \"{displayName}\" start= auto");
        RunSc($"description {serviceName} \"Synthetic HTTP/HTTPS monitoring with SMTP alerts.\"");
    }

    public static void EnsureServiceBinaryPath(string serviceName, string installRoot)
    {
        var expected = File.Exists(GetExpectedServiceExePath(installRoot))
            ? GetExpectedServiceExePath(installRoot)
            : Path.Combine(installRoot, ExeName);
        if (!File.Exists(expected))
        {
            return;
        }

        var current = ServicePathHelper.GetServiceExePath(serviceName);
        if (string.IsNullOrEmpty(current))
        {
            return;
        }

        if (string.Equals(Path.GetFullPath(current), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        RunSc($"config {serviceName} binPath= \"{expected}\"");
    }

    public static bool IsServiceRunning(string serviceName)
    {
        try
        {
            using var c = new ServiceController(serviceName);
            return c.Status == ServiceControllerStatus.Running;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static void StopServiceIfRunning(string serviceName)
    {
        try
        {
            using var c = new ServiceController(serviceName);
            if (c.Status == ServiceControllerStatus.Running)
            {
                c.Stop();
                c.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60));
            }
        }
        catch (InvalidOperationException)
        {
            // service does not exist
        }
    }

    public static void StartServiceIfExists(string serviceName)
    {
        using var c = new ServiceController(serviceName);
        if (c.Status != ServiceControllerStatus.Running)
        {
            c.Start();
            c.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
        }
    }

    public static void DeleteService(string serviceName)
    {
        StopServiceIfRunning(serviceName);
        RunSc($"delete {serviceName}");
    }

    /// <summary>
    /// Configure Windows to restart the service after crashes (first, second, and subsequent failures).
    /// Best-effort: failures are ignored so install/upgrade still succeeds under restrictive policy.
    /// </summary>
    public static void ConfigureServiceRecovery(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return;
        }

        // reset= seconds before failure count resets; actions= restart/delay_ms (up to three actions).
        TryRunScNoThrow($"failure {serviceName} reset= 86400 actions= restart/60000/restart/60000/restart/60000");
        TryRunScNoThrow($"failureflag {serviceName} 1");
    }

    private static void TryRunScNoThrow(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe"),
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null)
            {
                return;
            }

            p.WaitForExit();
        }
        catch
        {
            // ignore — recovery policy is optional
        }
    }

    private static void RunSc(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe"),
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start sc.exe");
        p.WaitForExit();
        if (p.ExitCode is not (0 or 1060))
        {
            var err = p.StandardError.ReadToEnd();
            var @out = p.StandardOutput.ReadToEnd();
            throw new InvalidOperationException($"sc.exe failed (exit {p.ExitCode}): {err}{@out}".Trim());
        }
    }

    public static void RegisterProgramsAndFeatures(
        string arpKeyName,
        string displayName,
        string publisher,
        string installLocation,
        string uninstallCommand,
        string exePathForIcon)
    {
        using var key = Registry.LocalMachine.CreateSubKey(
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{arpKeyName}",
            true);
        if (key is null)
        {
            throw new InvalidOperationException("Could not open Uninstall registry key.");
        }

        var vi = FileVersionInfo.GetVersionInfo(exePathForIcon);
        var displayVersion = string.IsNullOrWhiteSpace(vi.ProductVersion)
            ? (vi.FileVersion?.Trim() ?? "0.0.0")
            : vi.ProductVersion.Trim();

        var sizeKb = 0;
        if (Directory.Exists(installLocation))
        {
            long bytes = 0;
            foreach (var f in Directory.EnumerateFiles(installLocation, "*", SearchOption.AllDirectories))
            {
                bytes += new FileInfo(f).Length;
            }

            sizeKb = (int)(bytes / 1024);
        }

        key.SetValue("DisplayName", displayName);
        key.SetValue("DisplayVersion", displayVersion);
        key.SetValue("Publisher", publisher);
        key.SetValue("InstallLocation", installLocation);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        key.SetValue("UninstallString", uninstallCommand);
        key.SetValue("QuietUninstallString", uninstallCommand);
        key.SetValue("DisplayIcon", $"\"{exePathForIcon}\",0");
        key.SetValue("EstimatedSize", sizeKb, RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    public static void RemoveProgramsAndFeaturesEntry(string arpKeyName)
    {
        try
        {
            Registry.LocalMachine.DeleteSubKeyTree($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{arpKeyName}", throwOnMissingSubKey: false);
        }
        catch
        {
            // ignore
        }
    }
}
