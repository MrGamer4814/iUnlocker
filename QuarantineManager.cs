using System.Text.Json;

namespace IUnlocker;

internal static class QuarantineManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string QuarantineDirectory => Path.Combine(AppContext.BaseDirectory, "Quarantine");

    private static string MetadataFile => Path.Combine(QuarantineDirectory, "quarantine.json");

    public static IReadOnlyList<QuarantineItem> LoadItems()
    {
        try
        {
            if (!File.Exists(MetadataFile))
            {
                return [];
            }

            var json = File.ReadAllText(MetadataFile);
            return JsonSerializer.Deserialize<List<QuarantineItem>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static QuarantineItem QuarantineFile(string path, string reason, string source)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("Файл не найден.", path);
        }

        if (SamePath(path, Application.ExecutablePath))
        {
            throw new InvalidOperationException("Нельзя переместить в карантин запущенный iUnlocker.");
        }

        Directory.CreateDirectory(QuarantineDirectory);

        var items = LoadItems().ToList();
        var id = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "_" + Guid.NewGuid().ToString("N")[..8];
        var safeName = MakeSafeFileName(Path.GetFileName(path));
        var quarantinedPath = Path.Combine(QuarantineDirectory, $"{id}_{safeName}");

        File.Move(path, quarantinedPath);

        var item = new QuarantineItem(
            id,
            Path.GetFileName(path),
            Path.GetFullPath(path),
            quarantinedPath,
            reason,
            source,
            DateTime.UtcNow);

        items.Add(item);
        SaveItems(items);
        return item;
    }

    public static void Restore(QuarantineItem item)
    {
        if (!File.Exists(item.QuarantinedPath))
        {
            throw new FileNotFoundException("Файл в карантине не найден.", item.QuarantinedPath);
        }

        if (File.Exists(item.OriginalPath))
        {
            throw new IOException("По исходному пути уже существует файл. Восстановление отменено.");
        }

        var directory = Path.GetDirectoryName(item.OriginalPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Move(item.QuarantinedPath, item.OriginalPath);
        RemoveItem(item.Id);
    }

    public static void Delete(QuarantineItem item)
    {
        if (File.Exists(item.QuarantinedPath))
        {
            File.Delete(item.QuarantinedPath);
        }

        RemoveItem(item.Id);
    }

    private static void RemoveItem(string id)
    {
        var items = LoadItems()
            .Where(item => !item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        SaveItems(items);
    }

    private static void SaveItems(List<QuarantineItem> items)
    {
        Directory.CreateDirectory(QuarantineDirectory);
        File.WriteAllText(MetadataFile, JsonSerializer.Serialize(items, JsonOptions));
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            return Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string MakeSafeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = fileName.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var safe = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "file.bin" : safe;
    }
}

internal sealed record QuarantineItem(
    string Id,
    string Name,
    string OriginalPath,
    string QuarantinedPath,
    string Reason,
    string Source,
    DateTime CreatedAtUtc)
{
    public string CreatedAtLocal => CreatedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
}
