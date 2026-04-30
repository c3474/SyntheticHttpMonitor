using System.Management;

namespace SyntheticHttpMonitor.Setup;

internal static class ServicePathHelper
{
    public static string? GetServiceBinaryDirectory(string serviceName)
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

                var exe = ExtractExe(pathName.Trim());
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
            // ignored
        }

        return null;
    }

    /// <summary>Returns the full path to the service process executable, or null.</summary>
    public static string? GetServiceExePath(string serviceName)
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

                var exe = ExtractExe(pathName.Trim());
                if (string.IsNullOrEmpty(exe))
                {
                    continue;
                }

                return Environment.ExpandEnvironmentVariables(exe);
            }
        }
        catch
        {
            // ignored
        }

        return null;
    }

    private static string? ExtractExe(string raw)
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
        if (idx < 0)
        {
            return null;
        }

        return raw[..(idx + 4)];
    }
}
