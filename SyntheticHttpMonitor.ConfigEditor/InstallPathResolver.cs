using System.Management;

namespace SyntheticHttpMonitor.ConfigEditor;

internal static class InstallPathResolver
{
    /// <summary>Tries to find the install folder from the SyntheticHttpMonitor Windows service binary path.</summary>
    public static string? TryGetInstallDirectoryFromService(string serviceName = "SyntheticHttpMonitor")
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT PathName FROM Win32_Service WHERE Name = '{serviceName.Replace("'", "''")}'");
            foreach (var o in searcher.Get())
            {
                var pathName = o["PathName"]?.ToString();
                if (string.IsNullOrWhiteSpace(pathName))
                {
                    continue;
                }

                var exe = ExtractQuotedExe(pathName.Trim());
                if (string.IsNullOrEmpty(exe))
                {
                    continue;
                }

                exe = Environment.ExpandEnvironmentVariables(exe);
                return Path.GetDirectoryName(exe);
            }
        }
        catch
        {
            // ignore — caller falls back to manual open
        }

        return null;
    }

    private static string? ExtractQuotedExe(string raw)
    {
        if (raw.Length >= 2 && raw[0] == '"')
        {
            var end = raw.IndexOf('"', 1);
            if (end > 1)
            {
                return raw[1..end];
            }
        }

        var idx = raw.LastIndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            return raw[..(idx + 4)];
        }

        var space = raw.IndexOf(' ');
        return space > 0 ? raw[..space] : null;
    }
}
