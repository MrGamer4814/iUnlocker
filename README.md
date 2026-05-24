# IUnlocker

Небольшая WinForms-утилита для просмотра автозагрузки Windows.

В главном меню есть:

- `Автозагрузка` - просмотр и редактирование найденных записей автозапуска;
- `Проводник` - внутренний файловый браузер без запуска `explorer.exe`.
- `Редактор реестра` - простой редактор live/offline-реестра;
- `SAM/SYSTEM` - просмотр пользователей offline-Windows/live-Windows и базовой информации SYSTEM; управление локальными пользователями доступно только в live-Windows.

При запуске программа показывает окно выбора диска. Там видно, запущена ли утилита из WinPE, какой диск является `X:\` WinPE, и на каком диске найдена папка Windows. В WinPE автозагрузка читается из выбранной offline-Windows, а не из временной среды WinPE.

В автозагрузке доступны удаление поддерживаемых записей, вкладка подозрительных элементов и открытие найденного файла во внутреннем проводнике IUnlocker.

Показывает:

- `Run`, `RunOnce`, `RunOnceEx`, `RunServices`, `Policies\Explorer\Run`;
- папки автозагрузки текущего пользователя и всех пользователей;
- `Winlogon` (`Shell`, `Userinit`, `Notify` и похожие значения);
- `CMDLINE` / `Command Processor\AutoRun` / `SYSTEM\Setup\CmdLine` и `SetupType`;
- `BootExecute` и похожие значения Session Manager;
- `AppInit_DLLs`;
- `IFEO` debugger hijacks и `SilentProcessExit`;
- Explorer shell extensions, BHO и похожие расширения;
- автоматические службы и драйверы, разделенные по вкладкам `Services` и `Drivers`;
- задания Планировщика, которые запускаются при входе пользователя или старте системы;
- WMI permanent consumers;
- LSA providers и Print Monitors.

## Запуск

Готовые сборки:

- `publish\IUnlocker.exe` - маленькая сборка, требует установленный .NET Desktop Runtime 8;
- `publish-standalone\IUnlocker.exe` - автономная сборка для Windows x64.

Из исходников:

```powershell
dotnet run --project .\IUnlocker.csproj
```
