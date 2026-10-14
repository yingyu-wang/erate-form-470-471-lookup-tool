namespace ERateLookup.Desktop;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        try { Application.Run(new MainForm()); }
        catch (Exception ex) { ERateLookup.Core.AppLog.Error(ex, "Application start"); MessageBox.Show("Application could not start: " + ex.Message, "E-rate Lookup", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
