using System.Diagnostics;

namespace SyntheticHttpMonitor.Setup;

internal static class MonitorExeVersion
{
    /// <summary>Human-readable version from ProductVersion, else FileVersion.</summary>
    public static string Read(string exePath)
    {
        if (!File.Exists(exePath))
        {
            return "(file not found)";
        }

        try
        {
            var vi = FileVersionInfo.GetVersionInfo(exePath);
            if (!string.IsNullOrWhiteSpace(vi.ProductVersion))
            {
                return vi.ProductVersion.Trim();
            }

            return string.IsNullOrWhiteSpace(vi.FileVersion) ? "unknown" : vi.FileVersion.Trim();
        }
        catch
        {
            return "unknown";
        }
    }
}
