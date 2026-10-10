namespace ERate470Lookup.Desktop;
internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        try { Application.Run(new MainForm()); }
        catch(Exception ex) { MessageBox.Show("Application could not start: "+ex.Message,"E-rate Form 470 Lookup",MessageBoxButtons.OK,MessageBoxIcon.Error); }
    }
}
