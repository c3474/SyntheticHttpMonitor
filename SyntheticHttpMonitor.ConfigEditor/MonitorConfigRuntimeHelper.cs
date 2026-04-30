using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;
using System.Text;

namespace SyntheticHttpMonitor.ConfigEditor;

internal static class MonitorConfigRuntimeHelper
{
    internal const string MonitorServiceName = "SyntheticHttpMonitor";

    internal enum MonitorServiceState
    {
        Unknown,
        NotInstalled,
        Stopped,
        Running,
        StartPending,
        StopPending,
    }

    internal static bool IsFileAccessDenied(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is UnauthorizedAccessException)
            {
                return true;
            }

            if (e is IOException io && (unchecked((uint)io.HResult) == 0x80070005))
            {
                return true;
            }

            if (e.Message.Contains("denied", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsSavedPathActiveInstallAppSettings(string? savedPath)
    {
        if (string.IsNullOrWhiteSpace(savedPath))
        {
            return false;
        }

        var dir = InstallPathResolver.TryGetInstallDirectoryFromService(MonitorServiceName);
        if (string.IsNullOrEmpty(dir))
        {
            return false;
        }

        try
        {
            var expected = Path.GetFullPath(Path.Combine(dir, "appsettings.json"));
            var actual = Path.GetFullPath(savedPath);
            return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    internal static MonitorServiceState GetMonitorServiceState()
    {
        try
        {
            using var sc = new ServiceController(MonitorServiceName);
            sc.Refresh();
            return sc.Status switch
            {
                ServiceControllerStatus.Running => MonitorServiceState.Running,
                ServiceControllerStatus.Stopped => MonitorServiceState.Stopped,
                ServiceControllerStatus.StartPending => MonitorServiceState.StartPending,
                ServiceControllerStatus.StopPending => MonitorServiceState.StopPending,
                _ => MonitorServiceState.Unknown,
            };
        }
        catch (InvalidOperationException)
        {
            return MonitorServiceState.NotInstalled;
        }
        catch
        {
            return MonitorServiceState.Unknown;
        }
    }

    /// <summary>Attempts stop+start in the current user context (no UAC).</summary>
    internal static (bool Ok, string? Message) TryRestartMonitorServiceInCurrentSession()
    {
        try
        {
            using var sc = new ServiceController(MonitorServiceName);
            sc.Refresh();
            var wait = TimeSpan.FromSeconds(90);
            if (sc.Status == ServiceControllerStatus.Running)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, wait);
            }

            if (sc.Status != ServiceControllerStatus.Running)
            {
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, wait);
            }

            return (true, null);
        }
        catch (InvalidOperationException ex)
        {
            return (false, ex.Message);
        }
        catch (System.ServiceProcess.TimeoutException ex)
        {
            return (false, ex.Message);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Runs a short PowerShell script with UAC elevation (Verb runas).</summary>
    internal static (int ExitCode, bool UserCancelled) RunElevatedPowerShell(string scriptBody)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(scriptBody));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var p = Process.Start(psi);
            if (p is null)
            {
                return (-1, false);
            }

            p.WaitForExit();
            return (p.ExitCode, false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User cancelled UAC
            return (-1, true);
        }
    }

    internal static string EscapePowerShellSingleQuoted(string s) => s.Replace("'", "''", StringComparison.Ordinal);

    /// <summary>Exit 0 = success; 1 = file copy failed; 2 = file OK but restart failed; user cancel via Win32Exception.</summary>
    internal static (int ExitCode, bool UserCancelled) TrySaveViaElevationAndRestart(
        string tempJsonPath,
        string destinationPath,
        bool restartService)
    {
        var t = EscapePowerShellSingleQuoted(tempJsonPath);
        var d = EscapePowerShellSingleQuoted(destinationPath);
        var n = EscapePowerShellSingleQuoted(MonitorServiceName);

        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine("try {");
        sb.AppendLine($"  Copy-Item -LiteralPath '{t}' -Destination '{d}' -Force");
        sb.AppendLine($"  Remove-Item -LiteralPath '{t}' -Force -ErrorAction SilentlyContinue");
        sb.AppendLine("} catch { exit 1 }");
        if (restartService)
        {
            sb.AppendLine("try {");
            sb.AppendLine($"  $svc = Get-Service -Name '{n}' -ErrorAction SilentlyContinue");
            sb.AppendLine("  if ($null -ne $svc) {");
            sb.AppendLine($"    Restart-Service -Name '{n}' -Force -ErrorAction Stop");
            sb.AppendLine("  }");
            sb.AppendLine("  exit 0");
            sb.AppendLine("} catch { exit 2 }");
        }
        else
        {
            sb.AppendLine("exit 0");
        }

        return RunElevatedPowerShell(sb.ToString());
    }

    internal static (int ExitCode, bool UserCancelled) TryRestartServiceElevated()
    {
        var name = EscapePowerShellSingleQuoted(MonitorServiceName);
        var script =
            "$ErrorActionPreference = 'Stop'\n" +
            "try {\n" +
            $"  $svc = Get-Service -Name '{name}' -ErrorAction SilentlyContinue\n" +
            "  if ($null -eq $svc) { exit 0 }\n" +
            $"  Restart-Service -Name '{name}' -Force -ErrorAction Stop\n" +
            "  exit 0\n" +
            "} catch { exit 2 }\n";

        return RunElevatedPowerShell(script);
    }
}
