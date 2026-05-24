using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace IUnlocker;

public static class GitHubUpdater
{
    private const string DefaultOwner = "MrGamer4814";
    private const string DefaultRepo = "iUnlocker";
    private const string DefaultAssetName = "iUnlocker.exe";

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    public static async Task<GitHubUpdateInfo?> CheckAsync(CancellationToken cancellationToken)
    {
        var settings = LoadSettings();
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("iUnlocker-updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        var url = $"https://api.github.com/repos/{settings.Owner}/{settings.Repo}/releases/latest";
        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tagName = root.GetProperty("tag_name").GetString() ?? string.Empty;

        if (!TryParseVersion(tagName, out var latestVersion) ||
            NormalizeVersion(latestVersion) <= NormalizeVersion(CurrentVersion))
        {
            return null;
        }

        var asset = FindAsset(root.GetProperty("assets"), settings.AssetName)
            ?? throw new InvalidOperationException($"В последнем релизе GitHub нет файла {settings.AssetName}.");

        var downloadUrl = asset.GetProperty("browser_download_url").GetString()
            ?? throw new InvalidOperationException("GitHub не вернул ссылку на файл обновления.");

        return new GitHubUpdateInfo(
            latestVersion,
            tagName,
            root.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() ?? tagName : tagName,
            asset.GetProperty("name").GetString() ?? settings.AssetName,
            asset.TryGetProperty("size", out var sizeProperty) && sizeProperty.TryGetInt64(out var size) ? size : null,
            new Uri(downloadUrl));
    }

    public static async Task<string> DownloadAsync(
        GitHubUpdateInfo update,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var updateDirectory = Path.Combine(Path.GetTempPath(), "iUnlocker-update");
        Directory.CreateDirectory(updateDirectory);
        var targetPath = Path.Combine(updateDirectory, $"iUnlocker-{update.Version}.exe");

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("iUnlocker-updater");
        using var response = await client.GetAsync(update.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? update.SizeBytes;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(targetPath);

        var buffer = new byte[1024 * 128];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            if (totalBytes is > 0)
            {
                progress?.Report((int)Math.Clamp(received * 100 / totalBytes.Value, 0, 100));
            }
        }

        progress?.Report(100);
        return targetPath;
    }

    public static void StartSelfReplace(string downloadedFilePath)
    {
        var currentExecutable = Environment.ProcessPath ?? Application.ExecutablePath;
        var scriptPath = Path.Combine(Path.GetTempPath(), $"iUnlocker-update-{Guid.NewGuid():N}.cmd");
        var script = $"""
@echo off
setlocal
set "SOURCE={downloadedFilePath}"
set "TARGET={currentExecutable}"
set "PID={Environment.ProcessId}"
:wait
tasklist /FI "PID eq %PID%" | find "%PID%" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto wait
)
copy /Y "%SOURCE%" "%TARGET%" >nul
if errorlevel 1 (
    start "" "%TARGET%"
    exit /b 1
)
start "" "%TARGET%"
del "%SOURCE%" >nul 2>nul
del "%~f0" >nul 2>nul
""";

        File.WriteAllText(scriptPath, script, Encoding.Default);
        Process.Start(new ProcessStartInfo
        {
            FileName = scriptPath,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    private static GitHubUpdateSettings LoadSettings()
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "iUnlocker.update.json");
        if (!File.Exists(settingsPath))
        {
            return new GitHubUpdateSettings(DefaultOwner, DefaultRepo, DefaultAssetName);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            var root = document.RootElement;
            var owner = GetString(root, "owner", DefaultOwner);
            var repo = GetString(root, "repo", DefaultRepo);
            var assetName = GetString(root, "assetName", DefaultAssetName);
            return new GitHubUpdateSettings(owner, repo, assetName);
        }
        catch
        {
            return new GitHubUpdateSettings(DefaultOwner, DefaultRepo, DefaultAssetName);
        }
    }

    private static string GetString(JsonElement root, string name, string fallback)
    {
        return root.TryGetProperty(name, out var property) && !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!
            : fallback;
    }

    private static JsonElement? FindAsset(JsonElement assets, string preferredAssetName)
    {
        JsonElement? firstExecutable = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? string.Empty;
            if (name.Equals(preferredAssetName, StringComparison.OrdinalIgnoreCase))
            {
                return asset;
            }

            if (firstExecutable is null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                firstExecutable = asset;
            }
        }

        return firstExecutable;
    }

    private static bool TryParseVersion(string tagName, out Version version)
    {
        var normalized = tagName.Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out version!);
    }

    private static Version NormalizeVersion(Version version)
    {
        return new Version(
            Math.Max(version.Major, 0),
            Math.Max(version.Minor, 0),
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0));
    }

    private sealed record GitHubUpdateSettings(string Owner, string Repo, string AssetName);
}

public sealed record GitHubUpdateInfo(
    Version Version,
    string TagName,
    string Name,
    string AssetName,
    long? SizeBytes,
    Uri DownloadUri);
