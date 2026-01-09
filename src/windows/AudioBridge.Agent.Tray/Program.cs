namespace AudioBridge.Agent.Tray;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // 添加全局异常处理
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (sender, e) =>
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ThreadException: {e.Exception}\n");
            MessageBox.Show($"发生未处理的异常：\n{e.Exception.Message}", "AudioBridge 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UnhandledException: {e.ExceptionObject}\n");
            MessageBox.Show($"发生严重错误：\n{e.ExceptionObject}", "AudioBridge 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        try
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
        catch (Exception ex)
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Main Exception: {ex}\n");
            MessageBox.Show($"程序启动失败：\n{ex.Message}\n\n详情已写入 crash.log", "AudioBridge 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }    
}