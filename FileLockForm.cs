using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace IUnlocker;

public sealed class FileLockForm : Form
{
    private readonly string _filePath;
    private readonly ListView _processList = new();
    private readonly Label _statusLabel = new();
    private readonly Button _refreshButton = new();
    private readonly Button _terminateButton = new();

    public FileLockForm(string filePath)
    {
        _filePath = filePath;
        BuildInterface();
        Shown += async (_, _) => await RefreshLocksAsync();
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - кто блокирует файл";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 420);
        ClientSize = new Size(900, 540);
        UiTheme.ApplyForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var pathLabel = new Label
        {
            Text = _filePath,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Margin = new Padding(0, 0, 0, 10),
        };

        _processList.Dock = DockStyle.Fill;
        _processList.View = View.Details;
        _processList.FullRowSelect = true;
        _processList.HideSelection = false;
        _processList.GridLines = true;
        _processList.Columns.Add("Процесс", 220);
        _processList.Columns.Add("PID", 90);
        _processList.Columns.Add("Тип", 150);
        _processList.Columns.Add("Путь", 400);
        _processList.SelectedIndexChanged += (_, _) => _terminateButton.Enabled = _processList.SelectedItems.Count == 1;
        UiTheme.StyleListView(_processList);

        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = UiTheme.MutedText;
        _statusLabel.Padding = new Padding(0, 8, 0, 8);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
        };
        _refreshButton.Text = "Обновить";
        _refreshButton.AutoSize = true;
        _refreshButton.Click += async (_, _) => await RefreshLocksAsync();
        UiTheme.StyleButton(_refreshButton, primary: true);

        _terminateButton.Text = "Завершить выбранный процесс";
        _terminateButton.AutoSize = true;
        _terminateButton.Enabled = false;
        _terminateButton.Click += async (_, _) => await TerminateSelectedProcessAsync();
        UiTheme.StyleButton(_terminateButton);
        actions.Controls.AddRange([_refreshButton, _terminateButton]);

        root.Controls.Add(pathLabel, 0, 0);
        root.Controls.Add(_processList, 0, 1);
        root.Controls.Add(_statusLabel, 0, 2);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);
    }

    private async Task RefreshLocksAsync()
    {
        _refreshButton.Enabled = false;
        _terminateButton.Enabled = false;
        _statusLabel.Text = "Поиск процессов...";
        try
        {
            var result = await Task.Run(() => RestartManager.GetLockingProcesses(_filePath));
            _processList.BeginUpdate();
            _processList.Items.Clear();
            foreach (var item in result.Processes)
            {
                var row = new ListViewItem(item.Name) { Tag = item.Pid };
                row.SubItems.Add(item.Pid.ToString());
                row.SubItems.Add(item.Type);
                row.SubItems.Add(item.FilePath);
                _processList.Items.Add(row);
            }
            _processList.EndUpdate();
            _statusLabel.Text = result.Message ?? (result.Processes.Count == 0
                ? "Блокирующие процессы не найдены. Файл может быть занят системным компонентом, который Restart Manager не показывает."
                : $"Найдено процессов: {result.Processes.Count}.");
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Не удалось проверить блокировку: {ex.Message}";
        }
        finally
        {
            _refreshButton.Enabled = true;
            _terminateButton.Enabled = _processList.SelectedItems.Count == 1;
        }
    }

    private async Task TerminateSelectedProcessAsync()
    {
        if (_processList.SelectedItems.Count != 1 || _processList.SelectedItems[0].Tag is not int pid)
        {
            return;
        }

        var name = _processList.SelectedItems[0].Text;
        if (MessageBox.Show(
                this,
                $"Завершить процесс {name} ({pid})?\r\n\r\nНесохранённые данные этого процесса могут быть потеряны.",
                "Кто блокирует файл",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                using var process = Process.GetProcessById(pid);
                process.Kill();
                process.WaitForExit(3000);
            });
            await RefreshLocksAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Не удалось завершить процесс", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static class RestartManager
    {
        private const int ErrorMoreData = 234;
        private const int ErrorSharingViolation = 32;
        private const int CchRmSessionKey = 32;

        public static LockQueryResult GetLockingProcesses(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                throw new FileNotFoundException("Файл не найден.", path);
            }

            var sessionKey = new StringBuilder(CchRmSessionKey + 1);
            var result = RmStartSession(out var handle, 0, sessionKey);
            if (result != 0)
            {
                throw new InvalidOperationException($"Restart Manager не запущен. Код: {result}.");
            }

            try
            {
                result = RmRegisterResources(handle, 1, [path], 0, null, 0, null);
                if (result == ErrorSharingViolation)
                {
                    return new LockQueryResult(
                        [],
                        "Файл используется ядром Windows. Для pagefile.sys, hiberfil.sys и системных hive-файлов нельзя определить обычный блокирующий процесс или снять блокировку во время работы Windows.");
                }

                if (result != 0)
                {
                    throw new InvalidOperationException($"Файл не зарегистрирован в Restart Manager. Код: {result}.");
                }

                uint needed = 0;
                uint count = 0;
                result = RmGetList(handle, out needed, ref count, null, out _);
                if (result == 0 && needed == 0)
                {
                    return new LockQueryResult([], null);
                }

                if (result != ErrorMoreData)
                {
                    throw new InvalidOperationException($"Не удалось получить список процессов. Код: {result}.");
                }

                var infos = new RmProcessInfo[needed];
                count = needed;
                result = RmGetList(handle, out needed, ref count, infos, out _);
                if (result != 0)
                {
                    throw new InvalidOperationException($"Не удалось прочитать список процессов. Код: {result}.");
                }

                return new LockQueryResult(infos.Take((int)count).Select(CreateLockingProcess).ToList(), null);
            }
            finally
            {
                RmEndSession(handle);
            }
        }

        private static LockingProcess CreateLockingProcess(RmProcessInfo info)
        {
            var filePath = string.Empty;
            try
            {
                using var process = Process.GetProcessById(info.Process.ProcessId);
                filePath = process.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                // The process can be protected or may exit while the list is read.
            }

            var name = string.IsNullOrWhiteSpace(info.AppName)
                ? $"PID {info.Process.ProcessId}"
                : info.AppName;
            return new LockingProcess(name, info.Process.ProcessId, GetTypeName(info.ApplicationType), filePath);
        }

        private static string GetTypeName(RmApplicationType type) => type switch
        {
            RmApplicationType.Service => "Служба",
            RmApplicationType.Explorer => "Проводник",
            RmApplicationType.Console => "Консоль",
            RmApplicationType.Critical => "Критический",
            _ => "Приложение",
        };

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, StringBuilder sessionKey);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(
            uint sessionHandle,
            uint fileNameCount,
            string[] fileNames,
            uint applicationCount,
            RmUniqueProcess[]? applications,
            uint serviceCount,
            string[]? serviceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(
            uint sessionHandle,
            out uint needed,
            ref uint processInfoCount,
            [In, Out] RmProcessInfo[]? affectedApps,
            out uint rebootReasons);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint sessionHandle);

        [StructLayout(LayoutKind.Sequential)]
        private struct RmUniqueProcess
        {
            public int ProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RmProcessInfo
        {
            public RmUniqueProcess Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ServiceShortName;
            public RmApplicationType ApplicationType;
            public uint AppStatus;
            public uint TssSessionId;
            [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
        }

        private enum RmApplicationType
        {
            Unknown,
            MainWindow,
            OtherWindow,
            Service,
            Explorer,
            Console,
            Critical,
        }
    }

    private sealed record LockingProcess(string Name, int Pid, string Type, string FilePath);
    private sealed record LockQueryResult(IReadOnlyList<LockingProcess> Processes, string? Message);
}
