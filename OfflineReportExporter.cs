using System.Text;

namespace IUnlocker;

internal static class OfflineReportExporter
{
    public static void Export(AppSession session, string filePath)
    {
        var report = BuildReport(session);
        File.WriteAllText(filePath, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string BuildReport(AppSession session)
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
}
