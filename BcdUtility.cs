using System.Diagnostics;
using System.Text;

namespace IUnlocker;

internal static class BcdUtility
{
    public static string? FindSelectedBcdStore(AppSession session)
    {
        var candidates = new[]
        {
            Path.Combine(session.DriveRoot, "Boot", "BCD"),
            Path.Combine(session.DriveRoot, "EFI", "Microsoft", "Boot", "BCD"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public static string GetEnumAllArguments(AppSession session)
    {
        if (session.IsWinPe && session.WindowsPath is not null && !session.DriveRoot.StartsWith("X:", StringComparison.OrdinalIgnoreCase))
        {
            var store = FindSelectedBcdStore(session)
                ?? throw new InvalidOperationException("BCD выбранной Windows не найден.");
            return $"/store {QuoteArgument(store)} /enum all";
        }

        return "/enum all";
    }

    public static string GetTargetText(AppSession session)
    {
        if (session.IsWinPe && session.WindowsPath is not null && !session.DriveRoot.StartsWith("X:", StringComparison.OrdinalIgnoreCase))
        {
            return FindSelectedBcdStore(session) is { } store
                ? $"Offline BCD: {store}"
                : "Offline BCD: не найден";
        }

        return "Текущая Windows";
    }

    public static CommandResult RunBcdEdit(string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bcdedit.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.Default,
                StandardErrorEncoding = Encoding.Default,
            },
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new CommandResult(process.ExitCode, output + error);
    }

    public static IReadOnlyList<BcdEntry> ParseEntries(string output)
    {
        var entries = new List<BcdEntry>();
        var lines = output.Replace("\r\n", "\n").Split('\n');
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var raw = new List<string>();
        string? title = null;

        void Flush()
        {
            if (current.Count == 0 && raw.Count == 0)
            {
                title = null;
                return;
            }

            entries.Add(new BcdEntry(
                title ?? string.Empty,
                GetValue(current, "identifier", "идентификатор"),
                GetValue(current, "description", "описание"),
                GetValue(current, "path", "путь"),
                GetValue(current, "safeboot"),
                GetValue(current, "testsigning"),
                GetValue(current, "recoveryenabled"),
                GetValue(current, "bootstatuspolicy"),
                string.Join(Environment.NewLine, raw)));
            current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            raw = [];
            title = null;
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                Flush();
                continue;
            }

            if (line.All(ch => ch == '-' || ch == '='))
            {
                continue;
            }

            raw.Add(line);
            var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                current[parts[0]] = parts[1].Trim();
            }
            else if (title is null)
            {
                title = line.Trim();
            }
        }

        Flush();
        return entries;
    }

    public static string QuoteArgument(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }

    private static string GetValue(Dictionary<string, string> values, params string[] names)
    {
        foreach (var name in names)
        {
            if (values.TryGetValue(name, out var value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}

internal sealed record CommandResult(int ExitCode, string Output);

internal sealed record BcdEntry(
    string Section,
    string Identifier,
    string Description,
    string Path,
    string SafeBoot,
    string TestSigning,
    string RecoveryEnabled,
    string BootStatusPolicy,
    string Raw);
