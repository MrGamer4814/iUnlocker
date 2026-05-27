using System.Text;
using System.Text.Encodings.Web;

namespace IUnlocker;

internal enum OfflineReportFormat
{
    Html,
    Text,
}

internal static class OfflineReportExporter
{
    public static void Export(AppSession session, string filePath, OfflineReportFormat format)
    {
        var report = format == OfflineReportFormat.Html
            ? BuildHtmlReport(session)
            : BuildTextReport(session);
        File.WriteAllText(filePath, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string BuildTextReport(AppSession session)
    {
        var builder = new StringBuilder();
        builder.AppendLine("iUnlocker report");
        builder.AppendLine($"Created: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Environment: {session.EnvironmentName}");
        builder.AppendLine($"Drive: {session.DriveRoot}");
        builder.AppendLine($"Windows: {session.WindowsPath ?? "not found"}");
        builder.AppendLine();

        AppendStartupReport(builder, session);
        AppendBcdReport(builder, session);

        return builder.ToString();
    }

    private static string BuildHtmlReport(AppSession session)
    {
        var startup = GetStartupReport(session);
        var bcd = GetBcdReport(session);
        var builder = new StringBuilder();

        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"ru\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine("<title>Отчёт iUnlocker</title>");
        builder.AppendLine("<style>");
        builder.AppendLine("""
:root{color-scheme:light;--bg:#f4f6fb;--panel:#fff;--text:#151922;--muted:#667085;--line:#d9dee9;--accent:#2563eb;--accent-soft:#e8f0ff;--warn:#fff7df;--bad:#fff0f0}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--text);font:14px/1.45 "Segoe UI",Arial,sans-serif}
.wrap{max-width:1180px;margin:0 auto;padding:28px}
.hero{background:linear-gradient(135deg,#ffffff,#eef4ff);border:1px solid var(--line);padding:24px;margin-bottom:18px}
h1{margin:0 0 8px;font-size:28px;font-weight:650}
h2{margin:0 0 14px;font-size:18px;font-weight:650}
.muted{color:var(--muted)}
.grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:10px;margin-top:18px}
.metric{background:var(--panel);border:1px solid var(--line);padding:12px}
.metric b{display:block;font-size:18px}
.card{background:var(--panel);border:1px solid var(--line);margin-top:14px;padding:18px}
.chips{display:flex;flex-wrap:wrap;gap:8px}
.chip{background:var(--accent-soft);color:#174ea6;border:1px solid #c8dafc;padding:5px 9px}
table{width:100%;border-collapse:collapse}
th,td{border-bottom:1px solid var(--line);padding:8px 9px;text-align:left;vertical-align:top}
th{background:#f8faff;font-weight:650;color:#344054;position:sticky;top:0}
tr.warn td{background:var(--warn)}
tr.bad td{background:var(--bad)}
.scroll{overflow:auto;border:1px solid var(--line);max-height:460px}
pre{margin:0;white-space:pre-wrap;word-break:break-word;font:12px/1.45 Consolas,"Courier New",monospace;background:#0f172a;color:#e5e7eb;padding:14px;max-height:420px;overflow:auto}
.empty{color:var(--muted);padding:18px;border:1px dashed var(--line);background:#fafbff}
@media(max-width:900px){.wrap{padding:14px}.grid{grid-template-columns:repeat(2,minmax(0,1fr))}}
""");
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<main class=\"wrap\">");
        builder.AppendLine("<section class=\"hero\">");
        builder.AppendLine("<h1>Отчёт iUnlocker</h1>");
        builder.AppendLine($"<div class=\"muted\">Создан: {Html(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))}</div>");
        builder.AppendLine("<div class=\"grid\">");
        AppendMetric(builder, "Среда", session.EnvironmentName);
        AppendMetric(builder, "Диск", session.DriveRoot);
        AppendMetric(builder, "Windows", session.WindowsPath ?? "не найдена");
        AppendMetric(builder, "Автозагрузка", startup.Entries.Count.ToString());
        builder.AppendLine("</div>");
        builder.AppendLine("</section>");

        AppendStartupHtml(builder, startup);
        AppendBcdHtml(builder, bcd);

        builder.AppendLine("</main>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static void AppendStartupReport(StringBuilder builder, AppSession session)
    {
        builder.AppendLine("=== Autostart ===");
        try
        {
            var result = session.IsWinPe && session.WindowsPath is not null && !session.DriveRoot.StartsWith("X:", StringComparison.OrdinalIgnoreCase)
                ? OfflineStartupScanner.Scan(session)
                : StartupScanner.Scan();

            builder.AppendLine($"Entries: {result.Entries.Count}");
            var groups = result.Entries
                .GroupBy(entry => entry.Category)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                builder.AppendLine($"  {group.Key}: {group.Count()}");
            }

            if (result.Warnings.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Warnings:");
                foreach (var warning in result.Warnings)
                {
                    builder.AppendLine($"- {warning}");
                }
            }

            builder.AppendLine();
            foreach (var entry in result.Entries.OrderBy(entry => entry.Category).ThenBy(entry => entry.Name))
            {
                builder.AppendLine($"[{entry.Category}] {entry.Name}");
                builder.AppendLine($"  Type: {entry.Type}");
                builder.AppendLine($"  Scope: {entry.Scope}");
                builder.AppendLine($"  Source: {entry.Source}");
                builder.AppendLine($"  Command: {entry.Command}");
                builder.AppendLine($"  Location: {entry.Location}");
                builder.AppendLine();
            }
        }
        catch (Exception ex)
        {
            builder.AppendLine($"Autostart error: {ex.Message}");
        }

        builder.AppendLine();
    }

    private static void AppendBcdReport(StringBuilder builder, AppSession session)
    {
        builder.AppendLine("=== BCD ===");
        builder.AppendLine(BcdUtility.GetTargetText(session));

        try
        {
            var result = BcdUtility.RunBcdEdit(BcdUtility.GetEnumAllArguments(session));
            builder.AppendLine($"Exit code: {result.ExitCode}");
            builder.AppendLine();
            builder.AppendLine(result.Output.TrimEnd());
        }
        catch (Exception ex)
        {
            builder.AppendLine($"BCD error: {ex.Message}");
        }

        builder.AppendLine();
    }

    private static StartupReportData GetStartupReport(AppSession session)
    {
        try
        {
            var result = session.IsWinPe && session.WindowsPath is not null && !session.DriveRoot.StartsWith("X:", StringComparison.OrdinalIgnoreCase)
                ? OfflineStartupScanner.Scan(session)
                : StartupScanner.Scan();
            return new StartupReportData(result.Entries, result.Warnings, null);
        }
        catch (Exception ex)
        {
            return new StartupReportData([], [], ex.Message);
        }
    }

    private static BcdReportData GetBcdReport(AppSession session)
    {
        try
        {
            var result = BcdUtility.RunBcdEdit(BcdUtility.GetEnumAllArguments(session));
            var entries = result.ExitCode == 0
                ? BcdUtility.ParseEntries(result.Output)
                : [];
            return new BcdReportData(BcdUtility.GetTargetText(session), result.ExitCode, result.Output.TrimEnd(), entries, null);
        }
        catch (Exception ex)
        {
            return new BcdReportData(BcdUtility.GetTargetText(session), -1, string.Empty, [], ex.Message);
        }
    }

    private static void AppendStartupHtml(StringBuilder builder, StartupReportData report)
    {
        builder.AppendLine("<section class=\"card\">");
        builder.AppendLine("<h2>Автозагрузка</h2>");
        if (!string.IsNullOrWhiteSpace(report.Error))
        {
            builder.AppendLine($"<div class=\"empty\">Ошибка: {Html(report.Error)}</div>");
            builder.AppendLine("</section>");
            return;
        }

        var groups = report.Entries
            .GroupBy(entry => entry.Category)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        builder.AppendLine("<div class=\"chips\">");
        foreach (var group in groups)
        {
            builder.AppendLine($"<span class=\"chip\">{Html(group.Key)}: {group.Count()}</span>");
        }
        builder.AppendLine("</div>");

        if (report.Warnings.Count > 0)
        {
            builder.AppendLine("<div class=\"card\" style=\"margin:14px 0 0;padding:12px;background:#fff8e8\">");
            builder.AppendLine("<b>Предупреждения</b>");
            builder.AppendLine("<ul>");
            foreach (var warning in report.Warnings)
            {
                builder.AppendLine($"<li>{Html(warning)}</li>");
            }
            builder.AppendLine("</ul>");
            builder.AppendLine("</div>");
        }

        if (report.Entries.Count == 0)
        {
            builder.AppendLine("<div class=\"empty\">Записи автозагрузки не найдены.</div>");
            builder.AppendLine("</section>");
            return;
        }

        builder.AppendLine("<div class=\"scroll\" style=\"margin-top:14px\">");
        builder.AppendLine("<table>");
        builder.AppendLine("<thead><tr><th>Категория</th><th>Имя</th><th>Тип</th><th>Область</th><th>Команда</th><th>Расположение</th></tr></thead>");
        builder.AppendLine("<tbody>");
        foreach (var entry in report.Entries.OrderBy(entry => entry.Category).ThenBy(entry => entry.Name))
        {
            builder.AppendLine("<tr>");
            builder.AppendLine($"<td>{Html(entry.Category)}</td>");
            builder.AppendLine($"<td>{Html(entry.Name)}</td>");
            builder.AppendLine($"<td>{Html(entry.Type)}</td>");
            builder.AppendLine($"<td>{Html(entry.Scope)}</td>");
            builder.AppendLine($"<td>{Html(entry.Command)}</td>");
            builder.AppendLine($"<td>{Html(entry.Location)}</td>");
            builder.AppendLine("</tr>");
        }
        builder.AppendLine("</tbody></table></div>");
        builder.AppendLine("</section>");
    }

    private static void AppendBcdHtml(StringBuilder builder, BcdReportData report)
    {
        builder.AppendLine("<section class=\"card\">");
        builder.AppendLine("<h2>BCD</h2>");
        builder.AppendLine($"<div class=\"muted\">{Html(report.Target)}. Код выхода: {report.ExitCode}</div>");
        if (!string.IsNullOrWhiteSpace(report.Error))
        {
            builder.AppendLine($"<div class=\"empty\">Ошибка: {Html(report.Error)}</div>");
            builder.AppendLine("</section>");
            return;
        }

        var visibleEntries = report.Entries.Where(IsVisibleBcdEntry).ToList();
        if (visibleEntries.Count > 0)
        {
            builder.AppendLine("<div class=\"scroll\" style=\"margin-top:14px\">");
            builder.AppendLine("<table>");
            builder.AppendLine("<thead><tr><th>Запись</th><th>ID</th><th>Файл</th><th>SafeBoot</th><th>Test</th><th>Recovery</th><th>Политика</th></tr></thead>");
            builder.AppendLine("<tbody>");
            foreach (var entry in visibleEntries)
            {
                var css = !string.IsNullOrWhiteSpace(entry.SafeBoot) || IsEnabled(entry.TestSigning)
                    ? " class=\"warn\""
                    : IsDisabled(entry.RecoveryEnabled) ? " class=\"bad\"" : string.Empty;
                builder.AppendLine($"<tr{css}>");
                builder.AppendLine($"<td>{Html(GetBcdDisplayName(entry))}</td>");
                builder.AppendLine($"<td>{Html(Dash(entry.Identifier))}</td>");
                builder.AppendLine($"<td>{Html(Dash(entry.Path))}</td>");
                builder.AppendLine($"<td>{Html(Dash(entry.SafeBoot))}</td>");
                builder.AppendLine($"<td>{Html(BoolText(entry.TestSigning))}</td>");
                builder.AppendLine($"<td>{Html(BoolText(entry.RecoveryEnabled))}</td>");
                builder.AppendLine($"<td>{Html(Dash(entry.BootStatusPolicy))}</td>");
                builder.AppendLine("</tr>");
            }
            builder.AppendLine("</tbody></table></div>");
        }

        builder.AppendLine("<div style=\"margin-top:14px\"><pre>");
        builder.AppendLine(Html(report.RawOutput));
        builder.AppendLine("</pre></div>");
        builder.AppendLine("</section>");
    }

    private static void AppendMetric(StringBuilder builder, string label, string value)
    {
        builder.AppendLine("<div class=\"metric\">");
        builder.AppendLine($"<span class=\"muted\">{Html(label)}</span>");
        builder.AppendLine($"<b>{Html(value)}</b>");
        builder.AppendLine("</div>");
    }

    private static bool IsVisibleBcdEntry(BcdEntry entry)
    {
        var text = $"{entry.Section} {entry.Identifier} {entry.Description} {entry.Path}";
        return entry.Identifier.Equals("{bootmgr}", StringComparison.OrdinalIgnoreCase) ||
               entry.Identifier.Equals("{current}", StringComparison.OrdinalIgnoreCase) ||
               entry.Identifier.Equals("{default}", StringComparison.OrdinalIgnoreCase) ||
               ContainsAny(text, "Windows", "Загрузка Windows", "Диспетчер загрузки", "Recovery", "Восстановление", "Resume", "Возобновление", "winload", "winresume", "bootmgfw");
    }

    private static string GetBcdDisplayName(BcdEntry entry)
    {
        if (entry.Identifier.Equals("{bootmgr}", StringComparison.OrdinalIgnoreCase) ||
            entry.Path.Contains("bootmgfw", StringComparison.OrdinalIgnoreCase))
        {
            return "Диспетчер загрузки Windows";
        }

        if (entry.Path.Contains("winload", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(entry.Description) ? "Загрузчик Windows" : entry.Description;
        }

        if (entry.Path.Contains("winresume", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(entry.Description) ? "Возобновление Windows" : entry.Description;
        }

        return !string.IsNullOrWhiteSpace(entry.Description)
            ? entry.Description
            : string.IsNullOrWhiteSpace(entry.Section) ? "Запись BCD" : entry.Section;
    }

    private static string Html(string value)
    {
        return HtmlEncoder.Default.Encode(value);
    }

    private static string Dash(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private static string BoolText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        if (IsEnabled(value))
        {
            return "Включено";
        }

        if (IsDisabled(value))
        {
            return "Отключено";
        }

        return value;
    }

    private static bool IsEnabled(string value)
    {
        return value.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Да", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("On", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("True", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDisabled(string value)
    {
        return value.Equals("No", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Нет", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Off", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("False", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record StartupReportData(
        IReadOnlyList<StartupEntry> Entries,
        IReadOnlyList<string> Warnings,
        string? Error);

    private sealed record BcdReportData(
        string Target,
        int ExitCode,
        string RawOutput,
        IReadOnlyList<BcdEntry> Entries,
        string? Error);
}
