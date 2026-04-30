using System.Windows.Forms;

namespace SyntheticHttpMonitor.Setup;

internal static class UninstallRunner
{
    private const string DefaultServiceName = "SyntheticHttpMonitor";

    /// <summary>Returns process exit code (0 = success).</summary>
    public static int Run(bool silent, string serviceName = DefaultServiceName)
    {
        try
        {
            InstallOperations.StopServiceIfRunning(serviceName);
            InstallOperations.DeleteService(serviceName);
            Thread.Sleep(2000);
            InstallOperations.RemoveProgramsAndFeaturesEntry(serviceName);

            if (!silent)
            {
                MessageBox.Show(
                    "The Synthetic HTTP Monitor service was removed. The install folder was not deleted (your appsettings.json under Resources, or next to the service EXE, is still there).",
                    "Uninstall",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                MessageBox.Show(ex.Message, "Uninstall failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return 1;
        }
    }
}
