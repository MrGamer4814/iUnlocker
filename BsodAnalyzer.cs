using System.Diagnostics.Eventing.Reader;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace IUnlocker;

internal static class BsodAnalyzer
{
    private const int MaxDumpScanBytes = 64 * 1024 * 1024;

    private static readonly Dictionary<string, string> BugCheckNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["0x0000000A"] = "IRQL_NOT_LESS_OR_EQUAL",
        ["0x0000001A"] = "MEMORY_MANAGEMENT",
        ["0x0000003B"] = "SYSTEM_SERVICE_EXCEPTION",
        ["0x00000050"] = "PAGE_FAULT_IN_NONPAGED_AREA",
        ["0x0000007E"] = "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED",
        ["0x0000009F"] = "DRIVER_POWER_STATE_FAILURE",
        ["0x000000C2"] = "BAD_POOL_CALLER",
        ["0x000000D1"] = "DRIVER_IRQL_NOT_LESS_OR_EQUAL",
        ["0x00000124"] = "WHEA_UNCORRECTABLE_ERROR",
        ["0x00000133"] = "DPC_WATCHDOG_VIOLATION",
        ["0x00000139"] = "KERNEL_SECURITY_CHECK_FAILURE",
        ["0x00000154"] = "UNEXPECTED_STORE_EXCEPTION",
        ["0x000000EF"] = "CRITICAL_PROCESS_DIED",
        ["0x000000F4"] = "CRITICAL_OBJECT_TERMINATION",
    };

    public static IReadOnlyList<BsodAnalysisRow> Analyze(AppSession session)
    {
        if (string.IsNullOrWhiteSpace(session.WindowsPath) || !Directory.Exists(session.WindowsPath))
        {
            return [];
        }

        var events = LoadBugCheckEvents(session).ToList();
        var rows = new List<BsodAnalysisRow>();
        foreach (var dumpFile in EnumerateDumpFiles(session.WindowsPath))
        {
            var info = new FileInfo(dumpFile);
            var relatedEvent = FindRelatedEvent(info, events);
            var drivers = ExtractDriverNames(dumpFile).ToList();
            var suspect = PickSuspectDriver(session.WindowsPath, drivers);
            rows.Add(new BsodAnalysisRow(
                info.Name,
                info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                relatedEvent?.BugCheckCode ?? string.Empty,
                relatedEvent?.BugCheckName ?? string.Empty,
                suspect.DriverName,
                suspect.Signature,
                FormatSize(info.Length),
                dumpFile,
                relatedEvent?.Summary ?? string.Empty,
                string.Join(", ", drivers.Take(10))));
        }

        foreach (var bugEvent in events.Where(item => rows.All(row => !SameDumpPath(row.Path, item.DumpPath))))
        {
            rows.Add(new BsodAnalysisRow(
                string.IsNullOrWhiteSpace(bugEvent.DumpPath) ? "(дамп не найден)" : Path.GetFileName(bugEvent.DumpPath),
                bugEvent.TimeCreated.ToString("yyyy-MM-dd HH:mm:ss"),
                bugEvent.BugCheckCode,
                bugEvent.BugCheckName,
                string.Empty,
                string.Empty,
                string.Empty,
                bugEvent.DumpPath,
                bugEvent.Summary,
                string.Empty));
        }

        return rows
            .OrderByDescending(row => row.TimeCreated, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> EnumerateDumpFiles(string windowsPath)
    {
        var minidump = Path.Combine(windowsPath, "Minidump");
        if (Directory.Exists(minidump))
        {
            foreach (var file in Directory.EnumerateFiles(minidump, "*.dmp", SearchOption.TopDirectoryOnly))
            {
                yield return file;
            }
        }

        var memoryDump = Path.Combine(windowsPath, "MEMORY.DMP");
        if (File.Exists(memoryDump))
        {
            yield return memoryDump;
        }
    }

    private static IEnumerable<BugCheckEvent> LoadBugCheckEvents(AppSession session)
    {
        var logPath = Path.Combine(session.WindowsPath!, "System32", "winevt", "Logs", "System.evtx");
        var isLive = !session.IsWinPe;
        EventLogQuery query;
        try
        {
            query = new EventLogQuery(
                isLive ? "System" : logPath,
                isLive ? PathType.LogName : PathType.FilePath,
                "*[System[(EventID=1001)]]")
            {
                ReverseDirection = true,
            };
        }
        catch
        {
            yield break;
        }

        EventLogReader reader;
        try
        {
            reader = new EventLogReader(query);
        }
        catch
        {
            yield break;
        }

        using (reader)
        {
        var count = 0;
        while (count < 80)
        {
            EventRecord? record;
            try
            {
                record = reader.ReadEvent();
            }
            catch
            {
                yield break;
            }

            if (record is null)
            {
                yield break;
            }

            using (record)
            {
                count++;
                var parsed = TryParseBugCheckEvent(record);
                if (parsed is not null)
                {
                    yield return parsed;
                }
            }
        }
        }
    }

    private static BugCheckEvent? TryParseBugCheckEvent(EventRecord record)
    {
        string xml;
        try
        {
            xml = record.ToXml();
        }
        catch
        {
            return null;
        }

        try
        {
            var document = XDocument.Parse(xml);
            var eventData = document.Descendants().Where(node => node.Name.LocalName == "Data").ToList();
            var values = eventData.Select(node => node.Value).Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            var named = eventData
                .Where(node => node.Attribute("Name") is not null)
                .ToDictionary(node => node.Attribute("Name")!.Value, node => node.Value, StringComparer.OrdinalIgnoreCase);

            var bugCode = FirstNonEmpty(
                TryNormalizeBugCheck(GetNamed(named, "BugcheckCode")),
                TryNormalizeBugCheck(values.FirstOrDefault(value => value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))),
                TryNormalizeBugCheck(values.FirstOrDefault(value => int.TryParse(value, out _))));
            var dumpPath = FirstNonEmpty(
                GetNamed(named, "DumpFile"),
                values.FirstOrDefault(value => value.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase)));

            var summary = string.Join("; ", values.Take(8));
            return string.IsNullOrWhiteSpace(bugCode) && string.IsNullOrWhiteSpace(dumpPath)
                ? null
                : new BugCheckEvent(
                    record.TimeCreated?.ToLocalTime() ?? DateTime.MinValue,
                    bugCode,
                    GetBugCheckName(bugCode),
                    dumpPath,
                    summary);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> ExtractDriverNames(string dumpPath)
    {
        var file = new FileInfo(dumpPath);
        if (!file.Exists || file.Length <= 0)
        {
            yield break;
        }

        var length = (int)Math.Min(file.Length, MaxDumpScanBytes);
        byte[] bytes;
        try
        {
            bytes = new byte[length];
            using var stream = File.OpenRead(dumpPath);
            _ = stream.Read(bytes, 0, length);
        }
        catch
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var driver in ExtractDriverNamesFromText(Encoding.ASCII.GetString(bytes)))
        {
            if (seen.Add(driver))
            {
                yield return driver;
            }
        }

        foreach (var driver in ExtractDriverNamesFromText(Encoding.Unicode.GetString(bytes)))
        {
            if (seen.Add(driver))
            {
                yield return driver;
            }
        }
    }

    private static IEnumerable<string> ExtractDriverNamesFromText(string text)
    {
        foreach (Match match in Regex.Matches(text, @"(?i)([a-z0-9_\-\.]{2,64}\.sys)"))
        {
            var name = match.Groups[1].Value;
            if (!IsNoiseDriverName(name))
            {
                yield return name;
            }
        }
    }

    private static (string DriverName, string Signature) PickSuspectDriver(string windowsPath, IReadOnlyList<string> drivers)
    {
        foreach (var driver in drivers)
        {
            var path = Path.Combine(windowsPath, "System32", "drivers", driver);
            if (!File.Exists(path))
            {
                continue;
            }

            var signature = FileSignatureVerifier.Verify(path);
            if (!signature.IsValid || !signature.Publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
            {
                return (driver, string.IsNullOrWhiteSpace(signature.Status) ? "не проверено" : $"{signature.Status} {signature.Publisher}".Trim());
            }
        }

        var first = drivers.FirstOrDefault();
        return string.IsNullOrWhiteSpace(first)
            ? (string.Empty, string.Empty)
            : (first, string.Empty);
    }

    private static bool IsNoiseDriverName(string name)
    {
        return name.StartsWith(".", StringComparison.Ordinal) ||
               name.Contains("..", StringComparison.Ordinal) ||
               name.Equals("dump_storpor.sys", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("dump_dumpfve.sys", StringComparison.OrdinalIgnoreCase);
    }

    private static BugCheckEvent? FindRelatedEvent(FileInfo dump, IReadOnlyList<BugCheckEvent> events)
    {
        return events
            .Where(item =>
                SameDumpPath(dump.FullName, item.DumpPath) ||
                Math.Abs((item.TimeCreated - dump.LastWriteTime).TotalMinutes) <= 30)
            .OrderBy(item => Math.Abs((item.TimeCreated - dump.LastWriteTime).TotalSeconds))
            .FirstOrDefault();
    }

    private static bool SameDumpPath(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return Path.GetFileName(left).Equals(Path.GetFileName(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string TryNormalizeBugCheck(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        try
        {
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                var number = Convert.ToUInt64(trimmed[2..], 16);
                return $"0x{number:X8}";
            }

            if (ulong.TryParse(trimmed, out var decimalValue))
            {
                return $"0x{decimalValue:X8}";
            }
        }
        catch
        {
            return trimmed;
        }

        return trimmed;
    }

    private static string GetBugCheckName(string code)
    {
        return BugCheckNames.TryGetValue(code, out var name) ? name : string.Empty;
    }

    private static string GetNamed(Dictionary<string, string> values, string name)
    {
        return values.TryGetValue(name, out var value) ? value : string.Empty;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private sealed record BugCheckEvent(DateTime TimeCreated, string BugCheckCode, string BugCheckName, string DumpPath, string Summary);
}

internal sealed record BsodAnalysisRow(
    string DumpName,
    string TimeCreated,
    string BugCheckCode,
    string BugCheckName,
    string SuspectDriver,
    string Signature,
    string Size,
    string Path,
    string EventSummary,
    string DriverMentions);
