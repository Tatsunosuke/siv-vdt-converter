namespace VdtConverter;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new UI.MainForm(args.Length > 0 ? args[0] : null));
    }
}
