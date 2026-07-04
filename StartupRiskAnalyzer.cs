namespace IUnlocker;

internal static class StartupRiskAnalyzer
{
    public static StartupEntry Analyze(StartupEntry entry)
    {
        var reasons = new List<string>();
        var score = 0;
        var text = $"{entry.Command} {entry.Location}";
        var lower = text.ToLowerInvariant();

        if (IsBadSignature(entry.SignatureStatus))
        {
            score += 35;
            reasons.Add("проблема с подписью");
        }
        else if (entry.SignatureStatus.Contains("не подпис", StringComparison.OrdinalIgnoreCase) ||
                 entry.SignatureStatus.Contains("Unsigned", StringComparison.OrdinalIgnoreCase))
        {
            score += 18;
            reasons.Add("нет цифровой подписи");
        }

        if (IsSensitiveAutostartPoint(entry))
        {
            score += 30;
            reasons.Add("опасная точка автозапуска");
        }

        if (ContainsAny(lower, @"\appdata\", @"\temp\", @"\downloads\", @"\users\public\", @"\recycler\", @"\$recycle.bin\"))
        {
            score += 25;
            reasons.Add("файл в пользовательской или временной папке");
        }

        if (ContainsAny(lower, "powershell", "pwsh", "wscript", "cscript", "mshta", "regsvr32", "cmd.exe /c"))
        {
            score += 22;
            reasons.Add("скриптовый или системный запускатель");
        }

        if (ContainsAny(lower, ".ps1", ".vbs", ".js", ".jse", ".wsf", ".bat", ".cmd", ".scr"))
        {
            score += 18;
            reasons.Add("скрипт в автозагрузке");
        }

        if (lower.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("https://", StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
            reasons.Add("ссылка на сеть");
        }

        var target = TryGetExistingTargetPath(entry);
        if (target is null &&
            !entry.Location.StartsWith("HK", StringComparison.OrdinalIgnoreCase) &&
            !entry.Location.StartsWith("Offline", StringComparison.OrdinalIgnoreCase) &&
            !entry.Location.StartsWith("Task Scheduler:", StringComparison.OrdinalIgnoreCase))
        {
            score += 15;
            reasons.Add("файл не найден");
        }

        var level = score >= 50
            ? "Высокий"
            : score >= 20
                ? "Средний"
                : string.Empty;

        return entry with
        {
            RiskLevel = level,
            RiskDetails = string.IsNullOrWhiteSpace(level)
                ? string.Empty
                : string.Join("; ", reasons.Distinct(StringComparer.OrdinalIgnoreCase)),
        };
    }

    public static bool IsSuspicious(StartupEntry entry)
    {
        return entry.RiskLevel.Equals("Высокий", StringComparison.OrdinalIgnoreCase) ||
               entry.RiskLevel.Equals("Средний", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSensitiveAutostartPoint(StartupEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Command))
        {
            return false;
        }

        return entry.Category.Equals("IFEO", StringComparison.OrdinalIgnoreCase) ||
               entry.Category.Equals("CMDLINE", StringComparison.OrdinalIgnoreCase) ||
               entry.Category.Equals("WMI", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBadSignature(string status)
    {
        return status.Contains("поврежд", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("Запрещ", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("Invalid", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("Revoked", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryGetExistingTargetPath(StartupEntry entry)
    {
        if (File.Exists(entry.Location) || Directory.Exists(entry.Location))
        {
            return entry.Location;
        }

        foreach (var candidate in GetCommandPathCandidates(entry.Command))
        {
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCommandPathCandidates(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            yield break;
        }

        var expanded = Environment.ExpandEnvironmentVariables(command.Trim());
        if (expanded.StartsWith('"'))
        {
            var closingQuote = expanded.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                yield return expanded[1..closingQuote];
            }
        }

        var separators = new[] { " /", " -", " \t" };
        var end = expanded.Length;
        foreach (var separator in separators)
        {
            var index = expanded.IndexOf(separator, StringComparison.Ordinal);
            if (index > 0)
            {
                end = Math.Min(end, index);
            }
        }

        yield return expanded[..end].Trim('"', ' ');

        foreach (var extension in new[] { ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".sys" })
        {
            var index = expanded.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                yield return expanded[..(index + extension.Length)].Trim('"', ' ');
            }
        }
    }
}
