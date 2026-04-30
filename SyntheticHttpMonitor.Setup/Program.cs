using System.Windows.Forms;

namespace SyntheticHttpMonitor.Setup;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();

        if (args.Any(a => string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(UninstallRunner.Run(silent: true));
        }

        Application.Run(new SetupForm());
    }
}
