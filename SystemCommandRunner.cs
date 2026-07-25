using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace IUnlocker;

internal sealed record SystemCommand(string FileName, string Arguments);

internal static class SystemCommandRunner
{
    private const string ConsoleRunArgument = "--console-run";
    private static Process? _activeProcess;

    public static void Show(IWin32Window? owner, string title, params SystemCommand[] commands)
    {
        if (commands.Length == 0)
        {
            throw new ArgumentException("Не указана команда для выполнения.", nameof(commands));
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            throw new InvalidOperationException("Не удалось определить файл iUnlocker для запуска консольного окна.");
        }

        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(commands)));
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        startInfo.ArgumentList.Add(ConsoleRunArgument);
        startInfo.ArgumentList.Add(title);
        startInfo.ArgumentList.Add(payload);

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("Не удалось открыть консольное окно iUnlocker.");
        }
    }

    public static bool TryRunConsoleHost(string[] args)
    {
        if (args.Length != 3 || !string.Equals(args[0], ConsoleRunArgument, StringComparison.Ordinal))
        {
            return false;
        }

        var title = args[1];
        SystemCommand[]? commands;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(args[2]));
            commands = JsonSerializer.Deserialize<SystemCommand[]>(json);
        }
        catch (Exception ex)
        {
            ShowConsoleError($"Не удалось прочитать команду iUnlocker: {ex.Message}");
            return true;
        }

        if (commands is not { Length: > 0 })
        {
            ShowConsoleError("Не указаны команды для выполнения.");
            return true;
        }

        RunConsoleHost(title, commands);
        return true;
    }

    public static string GetToolPath(string fileName)
    {
        if (Path.IsPathRooted(fileName))
        {
            return fileName;
        }

        var systemPath = Path.Combine(Environment.SystemDirectory, fileName);
        return File.Exists(systemPath) ? systemPath : fileName;
    }

    private static void RunConsoleHost(string title, IEnumerable<SystemCommand> commands)
    {
        NativeConsole.Open(title);
        NativeConsole.DisableCloseButton();
        var encoding = BcdUtility.GetConsoleEncoding();
        Console.OutputEncoding = encoding;
        Console.InputEncoding = encoding;
        Console.WriteLine("iUnlocker - выполнение системной команды");
        Console.WriteLine();

        ApplicationConfiguration.Initialize();
        using var controlForm = new CommandConsoleControlForm(title, () => _activeProcess is not null, StopActiveProcess);
        var commandTask = Task.Run(() => RunCommands(commands));
        _ = commandTask.ContinueWith(_ =>
        {
            if (!controlForm.IsDisposed)
            {
                controlForm.BeginInvoke(controlForm.MarkCompleted);
            }
        }, TaskScheduler.Default);

        Application.Run(controlForm);
    }

    private static void RunCommands(IEnumerable<SystemCommand> commands)
    {
        try
        {
            foreach (var command in commands)
            {
                Console.WriteLine($"> {command.FileName} {command.Arguments}".TrimEnd());
                try
                {
                    using var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = GetToolPath(command.FileName),
                        Arguments = command.Arguments,
                        UseShellExecute = false,
                        CreateNoWindow = false,
                    }) ?? throw new InvalidOperationException($"Не удалось запустить {command.FileName}.");
                    _activeProcess = process;
                    process.WaitForExit();
                    Console.WriteLine();
                    Console.WriteLine($"Код завершения: {process.ExitCode}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine($"Ошибка: {ex.Message}");
                }
                finally
                {
                    _activeProcess = null;
                }

                Console.WriteLine();
            }

            Console.WriteLine("Готово. Закройте окно iUnlocker, чтобы завершить работу.");
        }
        finally
        {
            _activeProcess = null;
        }
    }

    private static void StopActiveProcess()
    {
        try
        {
            if (_activeProcess is { HasExited: false })
            {
                _activeProcess.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The process may already be finishing while the confirmation is visible.
        }

    }

    private static void ShowConsoleError(string message)
    {
        NativeConsole.Open("iUnlocker");
        Console.WriteLine(message);
        Console.WriteLine();
        Console.WriteLine("Нажмите любую клавишу, чтобы закрыть окно.");
        Console.ReadKey(intercept: true);
    }
}

internal static class NativeConsole
{
    public static void Open(string title)
    {
        if (!AllocConsole())
        {
            AttachConsole(0xFFFFFFFF);
        }

        SetConsoleTitle(title);
        var encoding = BcdUtility.GetConsoleEncoding();
        var output = new StreamWriter(Console.OpenStandardOutput(), encoding) { AutoFlush = true };
        Console.SetOut(output);
        Console.SetError(output);
        Console.SetIn(new StreamReader(Console.OpenStandardInput(), encoding));
    }

    public static void DisableCloseButton()
    {
        var window = GetConsoleWindow();
        if (window == IntPtr.Zero)
        {
            return;
        }

        var menu = GetSystemMenu(window, false);
        if (menu != IntPtr.Zero)
        {
            DeleteMenu(menu, 0xF060, 0x00000000);
            DrawMenuBar(window);
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool SetConsoleTitle(string title);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr window, bool revert);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DeleteMenu(IntPtr menu, uint position, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DrawMenuBar(IntPtr window);
}

internal sealed class CommandConsoleControlForm : Form
{
    private readonly Func<bool> _isRunning;
    private readonly Action _stopProcess;
    private readonly Label _status = new();
    private readonly Button _closeButton = new();
    private bool _completed;

    public CommandConsoleControlForm(string title, Func<bool> isRunning, Action stopProcess)
    {
        _isRunning = isRunning;
        _stopProcess = stopProcess;
        Text = title;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(390, 112);

        _status.Dock = DockStyle.Fill;
        _status.Padding = new Padding(16, 12, 16, 0);
        _status.Text = "Команда выполняется. Вывод отображается в отдельном окне iUnlocker.";

        _closeButton.Text = "Закрыть";
        _closeButton.AutoSize = true;
        _closeButton.Click += (_, _) => Close();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 45,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 12, 8),
        };
        buttons.Controls.Add(_closeButton);

        Controls.Add(_status);
        Controls.Add(buttons);
        FormClosing += ConfirmClose;
    }

    public void MarkCompleted()
    {
        _completed = true;
        _status.Text = "Команда завершена. Вывод остаётся в консольном окне iUnlocker.";
    }

    private void ConfirmClose(object? sender, FormClosingEventArgs args)
    {
        if (_completed || !_isRunning())
        {
            return;
        }

        if (MessageBox.Show(
                this,
                "Остановить выполняющуюся команду и закрыть окно?",
                "iUnlocker",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            args.Cancel = true;
            return;
        }

        _stopProcess();
    }
}
