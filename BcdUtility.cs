using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IUnlocker;

internal static class BcdUtility
{
    public static string? FindSelectedBcdStore(AppSession session)
    {
        return EnumerateBcdStoreCandidates(session).FirstOrDefault(File.Exists);
    }

    public static IReadOnlyList<string> GetBcdStoreCandidates(AppSession session)
    {
        return EnumerateBcdStoreCandidates(session).Where(File.Exists).ToList();
    }

    private static IEnumerable<string> EnumerateBcdStoreCandidates(AppSession session)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in GetPrimaryBcdCandidates(session.DriveRoot))
        {
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady ||
                (session.IsWinPe && drive.RootDirectory.FullName.StartsWith("X:", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            foreach (var candidate in GetPrimaryBcdCandidates(drive.RootDirectory.FullName))
            {
                if (seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }

        foreach (var volumeRoot in VolumeUtility.EnumerateVolumeRoots())
        {
            foreach (var candidate in GetPrimaryBcdCandidates(volumeRoot))
            {
                if (seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<string> GetPrimaryBcdCandidates(string driveRoot)
    {
        yield return Path.Combine(driveRoot, "Boot", "BCD");
        yield return Path.Combine(driveRoot, "EFI", "Microsoft", "Boot", "BCD");
        yield return Path.Combine(driveRoot, "Microsoft", "Boot", "BCD");
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
                StandardOutputEncoding = GetConsoleEncoding(),
                StandardErrorEncoding = GetConsoleEncoding(),
            },
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new CommandResult(process.ExitCode, output + error);
    }

    public static Encoding GetConsoleEncoding()
    {
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch
        {
            return Encoding.Default;
        }
    }

    public static IReadOnlyList<BcdEntry> ParseEntries(string output)
    {
        var entries = new List<BcdEntry>();
        var lines = output.Replace("\r\n", "\n").Split('\n');
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var raw = new List<string>();
        string? title = null;
        string? pendingTitle = null;

        void Flush()
        {
            if (current.Count == 0 && raw.Count == 0)
            {
                title = null;
                pendingTitle = null;
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
            pendingTitle = null;
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
                title = pendingTitle;
                continue;
            }

            raw.Add(line);
            var match = Regex.Match(line, @"^([^\s]+)\s{2,}(.+)$");
            if (match.Success)
            {
                current[match.Groups[1].Value] = match.Groups[2].Value.Trim();
            }
            else
            {
                pendingTitle = line.Trim();
                title ??= pendingTitle;
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
