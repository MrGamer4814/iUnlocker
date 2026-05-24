using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace IUnlocker;

public sealed class SamSystemToolsForm : Form
{
    private readonly AppSession _session;
    private readonly ListView _userList = new();
    private readonly Label _infoLabel = new();
    private readonly Button _addUserButton = new();
    private readonly Button _deleteUserButton = new();
    private readonly Button _enableUserButton = new();
    private readonly Button _disableUserButton = new();
    private readonly Button _resetPasswordButton = new();
    private readonly Button _addAdminButton = new();
    private readonly Button _removeAdminButton = new();

    private bool _liveManagementAvailable;

    public SamSystemToolsForm(AppSession session)
    {
        _session = session;
        BuildInterface();
        Load += (_, _) => LoadInfo();
    }

    private void BuildInterface()
    {
        Text = "iUnlocker - SAM/SYSTEM";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(820, 520);
        ClientSize = new Size(900, 600);
        UiTheme.ApplyForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "SAM/SYSTEM",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        };
        UiTheme.StyleTitle(title, 16F);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 10),
        };

        ConfigureButton(_addUserButton, "Добавить", (_, _) => AddUser());
        ConfigureButton(_deleteUserButton, "Удалить", (_, _) => DeleteUser());
        ConfigureButton(_enableUserButton, "Включить", (_, _) => SetUserEnabled(enabled: true));
        ConfigureButton(_disableUserButton, "Отключить", (_, _) => SetUserEnabled(enabled: false));
        ConfigureButton(_resetPasswordButton, "Пароль", (_, _) => ResetPassword());
        ConfigureButton(_addAdminButton, "В админы", (_, _) => SetAdministratorsMembership(add: true));
        ConfigureButton(_removeAdminButton, "Убрать админа", (_, _) => SetAdministratorsMembership(add: false));

        toolbar.Controls.AddRange([
            _addUserButton,
            _deleteUserButton,
            _enableUserButton,
            _disableUserButton,
            _resetPasswordButton,
            _addAdminButton,
            _removeAdminButton,
        ]);

        _userList.Dock = DockStyle.Fill;
        _userList.View = View.Details;
        _userList.FullRowSelect = true;
        _userList.GridLines = true;
        _userList.Columns.Add("Пользователь", 260);
        _userList.Columns.Add("RID", 90);
        _userList.Columns.Add("Пароль", 120);
        _userList.Columns.Add("Админ", 100);
        _userList.Columns.Add("Состояние", 130);
        _userList.SelectedIndexChanged += (_, _) => UpdateActionButtons();
        UiTheme.StyleListView(_userList);

        _infoLabel.AutoSize = true;
        _infoLabel.Padding = new Padding(0, 10, 0, 0);
        _infoLabel.ForeColor = UiTheme.MutedText;

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(toolbar, 0, 1);
        root.Controls.Add(_userList, 0, 2);
        root.Controls.Add(_infoLabel, 0, 3);
        Controls.Add(root);

        UpdateActionButtons();
    }

    private static void ConfigureButton(Button button, string text, EventHandler onClick)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Margin = new Padding(0, 0, 8, 8);
        UiTheme.StyleButton(button);
        button.Click += onClick;
    }

    private void LoadInfo()
    {
        _userList.Items.Clear();
        _liveManagementAvailable = false;

        if (_session.DriveRoot.StartsWith("X:", StringComparison.OrdinalIgnoreCase))
        {
            _infoLabel.Text = "X:\\ - это WinPE. Выберите диск с Windows или запустите просмотр live SAM/SYSTEM в обычной Windows.";
            UpdateActionButtons();
            return;
        }

        if (!_session.IsWinPe && IsCurrentSystemDrive(_session.DriveRoot))
        {
            LoadLiveInfo();
            return;
        }

        LoadOfflineInfo();
        UpdateActionButtons();
    }

    private void LoadLiveInfo()
    {
        try
        {
            using var sam = Registry.LocalMachine.OpenSubKey("SAM")
                ?? throw new InvalidOperationException("Не удалось открыть HKLM\\SAM. Запустите программу от администратора.");
            using var system = Registry.LocalMachine.OpenSubKey("SYSTEM")
                ?? throw new InvalidOperationException("Не удалось открыть HKLM\\SYSTEM.");

            LoadUsers(sam, @"HKLM\SAM");
            var computerName = ReadComputerName(system);
            var currentControlSet = ReadCurrentControlSet(system);
            _liveManagementAvailable = true;
            _infoLabel.Text = $"Live Windows. ComputerName: {computerName}. CurrentControlSet: {currentControlSet}. Пользователей: {_userList.Items.Count}.";
            UpdateActionButtons();
        }
        catch (Exception ex)
        {
            LoadLiveInfoFallback(ex.Message);
        }
    }

    private void LoadLiveInfoFallback(string samError)
    {
        try
        {
            foreach (var userName in LocalUsers.GetUserNames())
            {
                AddUserItem(LocalUsers.GetUserDetails(userName));
            }

            var computerName = Environment.MachineName;
            var controlSet = TryReadLiveControlSet();
            _liveManagementAvailable = true;
            _infoLabel.Text = $"Live Windows. ComputerName: {computerName}. CurrentControlSet: {controlSet}. HKLM\\SAM недоступен: {samError}. Пользователи показаны через Windows API.";
            UpdateActionButtons();
        }
        catch (Exception ex)
        {
            _infoLabel.Text = $"HKLM\\SAM недоступен: {samError}. Windows API тоже не сработал: {ex.Message}";
            UpdateActionButtons();
        }
    }

    private void LoadOfflineInfo()
    {
        if (_session.WindowsPath is null)
        {
            _infoLabel.Text = "На выбранном диске не найдена Windows.";
            return;
        }

        var samPath = Path.Combine(_session.WindowsPath, "System32", "config", "SAM");
        var systemPath = Path.Combine(_session.WindowsPath, "System32", "config", "SYSTEM");

        try
        {
            using var sam = OfflineRegistryHiveMount.Load(samPath, "IUnlocker_SAM_VIEW");
            using var system = OfflineRegistryHiveMount.Load(systemPath, "IUnlocker_SYSTEM_VIEW");

            LoadUsers(sam.Root, samPath);
            var computerName = ReadComputerName(system.Root);
            var currentControlSet = ReadCurrentControlSet(system.Root);

            _infoLabel.Text = $"Offline Windows: {_session.WindowsPath}. ComputerName: {computerName}. CurrentControlSet: {currentControlSet}. Пользователей: {_userList.Items.Count}.";
        }
        catch (Exception ex)
        {
            _infoLabel.Text = ex.Message;
        }

        UpdateActionButtons();
    }

    private void LoadUsers(RegistryKey samRoot, string source)
    {
        using var namesKey = samRoot.OpenSubKey(@"SAM\Domains\Account\Users\Names");
        if (namesKey is null)
        {
            _infoLabel.Text = "Не удалось открыть SAM\\Domains\\Account\\Users\\Names.";
            return;
        }

        foreach (var userName in namesKey.GetSubKeyNames().OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
        {
            using var userKey = namesKey.OpenSubKey(userName);
            var rid = ReadRidFromNameKey(userKey);
            AddUserItem(ReadOfflineUserDetails(samRoot, userName, rid));
        }
    }

    private void AddUserItem(UserDisplayInfo user)
    {
        var item = new ListViewItem(user.Name);
        item.SubItems.Add(user.Rid);
        item.SubItems.Add(user.HasPassword);
        item.SubItems.Add(user.IsAdmin);
        item.SubItems.Add(user.Enabled);
        _userList.Items.Add(item);
    }

    private static string ReadRidFromNameKey(RegistryKey? key)
    {
        if (key is null)
        {
            return "";
        }

        try
        {
            var kind = key.GetValueKind("");
            return kind == RegistryValueKind.None
                ? "0x" + Convert.ToString((int)key.GetValue("")!, 16).ToUpperInvariant()
                : Convert.ToString(key.GetValue("")) ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static UserDisplayInfo ReadOfflineUserDetails(RegistryKey samRoot, string userName, string rid)
    {
        var enabled = "неизвестно";
        var hasPassword = "неизвестно";

        try
        {
            var ridHex = rid.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? rid[2..].PadLeft(8, '0')
                : int.TryParse(rid, out var ridNumber)
                    ? ridNumber.ToString("X8")
                    : string.Empty;

            if (!string.IsNullOrWhiteSpace(ridHex))
            {
                using var userKey = samRoot.OpenSubKey($@"SAM\Domains\Account\Users\{ridHex}");
                var f = userKey?.GetValue("F") as byte[];
                var v = userKey?.GetValue("V") as byte[];

                if (f is { Length: > 0x38 })
                {
                    var flags = BitConverter.ToUInt16(f, 0x38);
                    enabled = (flags & 0x0001) != 0 ? "отключен" : "включен";
                }

                if (v is not null)
                {
                    hasPassword = v.Length > 0 ? "да/неизвестно" : "неизвестно";
                }
            }
        }
        catch
        {
            // Keep unknown values; offline SAM binary fields vary between versions.
        }

        return new UserDisplayInfo(userName, rid, hasPassword, "неизвестно", enabled);
    }

    private static string ReadComputerName(RegistryKey systemRoot)
    {
        var controlSet = ReadCurrentControlSet(systemRoot);
        using var key = systemRoot.OpenSubKey($@"{controlSet}\Control\ComputerName\ComputerName");
        return Convert.ToString(key?.GetValue("ComputerName")) ?? "не найден";
    }

    private static string ReadCurrentControlSet(RegistryKey systemRoot)
    {
        using var selectKey = systemRoot.OpenSubKey("Select");
        var current = selectKey?.GetValue("Current") is int value ? value : 1;
        return $"ControlSet{current:000}";
    }

    private static bool IsCurrentSystemDrive(string driveRoot)
    {
        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
        return !string.IsNullOrWhiteSpace(systemDrive) &&
               driveRoot.StartsWith(systemDrive, StringComparison.OrdinalIgnoreCase);
    }

    private static string TryReadLiveControlSet()
    {
        try
        {
            using var system = Registry.LocalMachine.OpenSubKey("SYSTEM");
            return system is null ? "не найден" : ReadCurrentControlSet(system);
        }
        catch
        {
            return "не найден";
        }
    }

    private void AddUser()
    {
        using var form = new UserCredentialsForm("Добавить пользователя", showUserName: true, showAdminCheckbox: true);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            if (_liveManagementAvailable)
            {
                LocalUsers.AddUser(form.UserName, form.Password);
                if (form.AddToAdministrators)
                {
                    LocalUsers.AddToAdministrators(form.UserName);
                }

                LoadInfo();
                _infoLabel.Text = $"Пользователь создан: {form.UserName}";
            }
        }
        catch (Exception ex)
        {
            ShowManageError(ex);
        }
    }

    private void DeleteUser()
    {
        var userName = GetSelectedUserName();
        if (userName is null)
        {
            return;
        }

        if (MessageBox.Show(this, $"Удалить пользователя \"{userName}\"?", "Пользователи", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            if (_liveManagementAvailable)
            {
                LocalUsers.DeleteUser(userName);
                LoadInfo();
                _infoLabel.Text = $"Пользователь удален: {userName}";
            }
        }
        catch (Exception ex)
        {
            ShowManageError(ex);
        }
    }

    private void SetUserEnabled(bool enabled)
    {
        var userName = GetSelectedUserName();
        if (userName is null)
        {
            return;
        }

        try
        {
            if (_liveManagementAvailable)
            {
                LocalUsers.SetEnabled(userName, enabled);
                LoadInfo();
                _infoLabel.Text = enabled ? $"Пользователь включен: {userName}" : $"Пользователь отключен: {userName}";
            }
        }
        catch (Exception ex)
        {
            ShowManageError(ex);
        }
    }

    private void ResetPassword()
    {
        var userName = GetSelectedUserName();
        if (userName is null)
        {
            return;
        }

        using var form = new UserCredentialsForm($"Новый пароль: {userName}", showUserName: false, showAdminCheckbox: false);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            if (_liveManagementAvailable)
            {
                LocalUsers.SetPassword(userName, form.Password);
                _infoLabel.Text = $"Пароль изменен: {userName}";
            }
        }
        catch (Exception ex)
        {
            ShowManageError(ex);
        }
    }

    private void SetAdministratorsMembership(bool add)
    {
        var userName = GetSelectedUserName();
        if (userName is null)
        {
            return;
        }

        try
        {
            if (_liveManagementAvailable)
            {
                if (add)
                {
                    LocalUsers.AddToAdministrators(userName);
                    _infoLabel.Text = $"Добавлен в администраторы: {userName}";
                }
                else
                {
                    LocalUsers.RemoveFromAdministrators(userName);
                    _infoLabel.Text = $"Удален из администраторов: {userName}";
                }
            }
        }
        catch (Exception ex)
        {
            ShowManageError(ex);
        }
    }

    private void UpdateActionButtons()
    {
        var hasSelection = GetSelectedUserName() is not null;
        var canManage = _liveManagementAvailable;
        _addUserButton.Enabled = canManage;
        _deleteUserButton.Enabled = canManage && hasSelection;
        _enableUserButton.Enabled = canManage && hasSelection;
        _disableUserButton.Enabled = canManage && hasSelection;
        _resetPasswordButton.Enabled = canManage && hasSelection;
        _addAdminButton.Enabled = canManage && hasSelection;
        _removeAdminButton.Enabled = canManage && hasSelection;
    }

    private string? GetSelectedUserName()
    {
        return _userList.SelectedItems.Count == 0 ? null : _userList.SelectedItems[0].Text;
    }

    private void ShowManageError(Exception ex)
    {
        MessageBox.Show(
            this,
            $"{ex.Message}\r\n\r\nДля управления пользователями запустите iUnlocker от администратора.",
            "Не удалось выполнить действие",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private static class LocalUsers
    {
        private const int NormalUserAccount = 0x0200;
        private const int MaxPreferredLength = -1;
        private const int NoError = 0;
        private const int MoreData = 234;
        private const int UserPrivilegeUser = 1;
        private const uint UfScript = 0x0001;
        private const uint UfAccountDisable = 0x0002;
        private const uint UfNormalAccount = 0x0200;

        public static IReadOnlyList<string> GetUserNames()
        {
            var names = new List<string>();
            var resumeHandle = 0;

            do
            {
                var result = NetUserEnum(
                    null,
                    1,
                    NormalUserAccount,
                    out var buffer,
                    MaxPreferredLength,
                    out var entriesRead,
                    out _,
                    ref resumeHandle);

                if (result is not NoError and not MoreData)
                {
                    throw new InvalidOperationException($"NetUserEnum вернул код {result}.");
                }

                try
                {
                    var current = buffer;
                    var itemSize = Marshal.SizeOf<UserInfo1>();
                    for (var index = 0; index < entriesRead; index++)
                    {
                        var info = Marshal.PtrToStructure<UserInfo1>(current);
                        if (!string.IsNullOrWhiteSpace(info.Name))
                        {
                            names.Add(info.Name);
                        }

                        current = IntPtr.Add(current, itemSize);
                    }
                }
                finally
                {
                    if (buffer != IntPtr.Zero)
                    {
                        NetApiBufferFree(buffer);
                    }
                }
            }
            while (resumeHandle != 0);

            return names.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static UserDisplayInfo GetUserDetails(string userName)
        {
            var result = NetUserGetInfo(null, userName, 1, out var buffer);
            ThrowIfError(result, "прочитать пользователя");

            try
            {
                var info = Marshal.PtrToStructure<UserInfo1>(buffer);
                return new UserDisplayInfo(
                    userName,
                    "",
                    info.PasswordAge == uint.MaxValue ? "неизвестно" : info.PasswordAge > 0 ? "да" : "нет/пустой",
                    IsAdministrator(userName) ? "да" : "нет",
                    (info.Flags & UfAccountDisable) != 0 ? "отключен" : "включен");
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    NetApiBufferFree(buffer);
                }
            }
        }

        public static void AddUser(string userName, string password)
        {
            var info = new UserInfo1
            {
                Name = userName,
                Password = password,
                Privilege = UserPrivilegeUser,
                HomeDirectory = null!,
                Comment = "Created by iUnlocker",
                Flags = UfScript | UfNormalAccount,
                ScriptPath = null!,
            };

            var result = NetUserAdd(null, 1, ref info, out _);
            ThrowIfError(result, "создать пользователя");
        }

        public static void DeleteUser(string userName)
        {
            ThrowIfError(NetUserDel(null, userName), "удалить пользователя");
        }

        public static void SetPassword(string userName, string password)
        {
            var info = new UserInfo1003 { Password = password };
            ThrowIfError(NetUserSetInfo(null, userName, 1003, ref info, out _), "изменить пароль");
        }

        public static void SetEnabled(string userName, bool enabled)
        {
            var flags = GetFlags(userName);
            flags = enabled ? flags & ~UfAccountDisable : flags | UfAccountDisable;
            var info = new UserInfo1008 { Flags = flags };
            ThrowIfError(NetUserSetInfo(null, userName, 1008, ref info, out _), enabled ? "включить пользователя" : "отключить пользователя");
        }

        public static void AddToAdministrators(string userName)
        {
            var member = new LocalGroupMembersInfo3 { DomainAndName = userName };
            ThrowIfError(NetLocalGroupAddMembers(null, GetAdministratorsGroupName(), 3, ref member, 1), "добавить в администраторы");
        }

        public static void RemoveFromAdministrators(string userName)
        {
            var member = new LocalGroupMembersInfo3 { DomainAndName = userName };
            ThrowIfError(NetLocalGroupDelMembers(null, GetAdministratorsGroupName(), 3, ref member, 1), "удалить из администраторов");
        }

        private static bool IsAdministrator(string userName)
        {
            try
            {
                var groupName = GetAdministratorsGroupName();
                var resumeHandle = IntPtr.Zero;
                var result = NetUserGetLocalGroups(
                    null,
                    userName,
                    0,
                    0,
                    out var buffer,
                    MaxPreferredLength,
                    out var entriesRead,
                    out _);

                if (result != NoError)
                {
                    return false;
                }

                try
                {
                    var current = buffer;
                    var itemSize = Marshal.SizeOf<LocalGroupUsersInfo0>();
                    for (var index = 0; index < entriesRead; index++)
                    {
                        var group = Marshal.PtrToStructure<LocalGroupUsersInfo0>(current);
                        if (group.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        current = IntPtr.Add(current, itemSize);
                    }
                }
                finally
                {
                    if (buffer != IntPtr.Zero)
                    {
                        NetApiBufferFree(buffer);
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static uint GetFlags(string userName)
        {
            var result = NetUserGetInfo(null, userName, 1, out var buffer);
            ThrowIfError(result, "прочитать пользователя");

            try
            {
                var info = Marshal.PtrToStructure<UserInfo1>(buffer);
                return info.Flags;
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    NetApiBufferFree(buffer);
                }
            }
        }

        private static string GetAdministratorsGroupName()
        {
            var account = (NTAccount)new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
                .Translate(typeof(NTAccount));
            return account.Value.Split('\\').Last();
        }

        private static void ThrowIfError(int code, string action)
        {
            if (code != NoError)
            {
                throw new InvalidOperationException($"Не удалось {action}. Код Windows API: {code}.");
            }
        }

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetUserEnum(
            string? serverName,
            int level,
            int filter,
            out IntPtr buffer,
            int preferredMaximumLength,
            out int entriesRead,
            out int totalEntries,
            ref int resumeHandle);

        [DllImport("netapi32.dll")]
        private static extern int NetApiBufferFree(IntPtr buffer);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetUserAdd(
            string? serverName,
            int level,
            ref UserInfo1 buffer,
            out int parameterError);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetUserDel(string? serverName, string userName);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetUserGetInfo(string? serverName, string userName, int level, out IntPtr buffer);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetUserSetInfo(
            string? serverName,
            string userName,
            int level,
            ref UserInfo1003 buffer,
            out int parameterError);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetUserSetInfo(
            string? serverName,
            string userName,
            int level,
            ref UserInfo1008 buffer,
            out int parameterError);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetLocalGroupAddMembers(
            string? serverName,
            string groupName,
            int level,
            ref LocalGroupMembersInfo3 buffer,
            int totalEntries);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetLocalGroupDelMembers(
            string? serverName,
            string groupName,
            int level,
            ref LocalGroupMembersInfo3 buffer,
            int totalEntries);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetUserGetLocalGroups(
            string? serverName,
            string userName,
            int level,
            int flags,
            out IntPtr buffer,
            int preferredMaximumLength,
            out int entriesRead,
            out int totalEntries);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct UserInfo1
        {
            public string Name;
            public string Password;
            public uint PasswordAge;
            public uint Privilege;
            public string HomeDirectory;
            public string Comment;
            public uint Flags;
            public string ScriptPath;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct UserInfo1003
        {
            public string Password;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UserInfo1008
        {
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct LocalGroupMembersInfo3
        {
            public string DomainAndName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct LocalGroupUsersInfo0
        {
            public string Name;
        }
    }

    private sealed record UserDisplayInfo(string Name, string Rid, string HasPassword, string IsAdmin, string Enabled);

    private sealed class UserCredentialsForm : Form
    {
        private readonly TextBox _userNameBox = new();
        private readonly TextBox _passwordBox = new();
        private readonly CheckBox _adminCheckBox = new();
        private readonly bool _showUserName;

        public UserCredentialsForm(string title, bool showUserName, bool showAdminCheckbox)
        {
            _showUserName = showUserName;
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(420, showUserName ? 170 : 125);

            var y = 12;
            if (showUserName)
            {
                Controls.Add(new Label { Text = "Имя пользователя", Location = new Point(12, y + 3), AutoSize = true });
                _userNameBox.Location = new Point(150, y);
                _userNameBox.Width = 250;
                Controls.Add(_userNameBox);
                y += 36;
            }

            Controls.Add(new Label { Text = "Пароль", Location = new Point(12, y + 3), AutoSize = true });
            _passwordBox.Location = new Point(150, y);
            _passwordBox.Width = 250;
            _passwordBox.UseSystemPasswordChar = true;
            Controls.Add(_passwordBox);
            y += 36;

            if (showAdminCheckbox)
            {
                _adminCheckBox.Text = "Добавить в администраторы";
                _adminCheckBox.AutoSize = true;
                _adminCheckBox.Location = new Point(150, y);
                Controls.Add(_adminCheckBox);
                y += 36;
            }

            var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(244, y), AutoSize = true };
            var cancelButton = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Location = new Point(324, y), AutoSize = true };
            Controls.Add(okButton);
            Controls.Add(cancelButton);
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        public string UserName => _showUserName ? _userNameBox.Text.Trim() : string.Empty;

        public string Password => _passwordBox.Text;

        public bool AddToAdministrators => _adminCheckBox.Checked;

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                if (_showUserName && string.IsNullOrWhiteSpace(UserName))
                {
                    MessageBox.Show(this, "Введите имя пользователя.", "Пользователи", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                    return;
                }
            }

            base.OnFormClosing(e);
        }
    }
}
