namespace IUnlocker;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) => ShowUnhandledException(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                LogUnhandledException(exception);
            }
        };

        using var diskSelection = new DiskSelectionForm();
        if (diskSelection.ShowDialog() != DialogResult.OK || diskSelection.SelectedSession is null)
        {
            return;
        }

        Application.Run(new MainMenuForm(diskSelection.SelectedSession));
    }    

    private static void ShowUnhandledException(Exception exception)
    {
        LogUnhandledException(exception);
        MessageBox.Show(
            exception.ToString(),
            "Необработанная ошибка iUnlocker",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static void LogUnhandledException(Exception exception)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "iUnlocker.error.log");
            File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n{exception}\r\n\r\n");
        }
        catch
        {
            // Ignore logging failures.
        }
    }
}
