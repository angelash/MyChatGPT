namespace AudioBridge.Agent.Tray;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        using var singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: "AudioBridge.Agent.Tray",
            createdNew: out var createdNew);

        if (!createdNew)
        {
            MessageBox.Show(
                "AudioBridge 已在运行（托盘）。",
                "AudioBridge",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }    
}