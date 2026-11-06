using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace CyberpunkVRInstaller;

internal sealed class ComponentInstaller
{
    private const string StateDirectoryName = ".cyberpunk-vr-components";

    private sealed record ComponentDefinition(string Name, string Repository, Func<string, bool> AssetFilter);

    private static readonly ComponentDefinition[] Components =
    [
        new("RED4ext", "WopsS/RED4ext", n => IsZip(n) && !Has(n, "symbols"))
    ];

    public static readonly (string Name, string Url)[] NexusComponents = [];

    private static bool IsZip(string name) => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    private static bool Has(string name, string value) => name.Contains(value, StringComparison.OrdinalIgnoreCase);

    public async Task<ComponentInstallResult> InstallGitHubComponentsAsync(
        string gameRoot, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureGameRoot(gameRoot);
        using var client = CreateClient();
        var installed = new List<string>();

        for (var index = 0; index < Components.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var component = Components[index];
            var start = (int)(100d * index / Components.Length);
            var end = (int)(100d * (index + 1) / Components.Length);
            var range = end - start;
            var downloadRange = Math.Max(1, range * 2 / 3);
            progress?.Report(new(start, $"Получение информации: {component.Name}…"));

            var release = await GetLatestReleaseAsync(client, component.Repository, cancellationToken);
            var asset = release.Assets.FirstOrDefault(a => component.AssetFilter(a.Name))
                ?? throw new InstallerException($"Для {component.Name} не найден подходящий ZIP в релизе {release.TagName}.");

            var temporary = Path.Combine(Path.GetTempPath(), $"cpvr-{Guid.NewGuid():N}.zip");
            try
            {
                await DownloadAsync(client, asset.DownloadUrl, temporary, component.Name, start,
                    downloadRange, progress, cancellationToken);
                InstallZip(gameRoot, temporary, $"{component.Name} {release.TagName}", progress,
                    start + downloadRange, Math.Max(1, range - downloadRange));
                installed.Add($"{component.Name} {release.TagName}");
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }

        progress?.Report(new(100, "Компоненты GitHub установлены"));
        return new ComponentInstallResult(installed);
    }

    public ComponentInstallResult InstallLocalArchives(
        string gameRoot, IReadOnlyList<string> archives, IProgress<InstallProgress>? progress = null)
    {
        EnsureGameRoot(gameRoot);
        if (archives.Count == 0) return new ComponentInstallResult([]);
        var installed = new List<string>();
        for (var i = 0; i < archives.Count; i++)
        {
            var path = archives[i];
            if (!IsSupportedArchive(path))
                throw new InstallerException($"Поддерживаются архивы ZIP, RAR и 7z. Не удалось открыть: {Path.GetFileName(path)}");
            var installPath = path;
            string? convertedArchive = null;
            try
            {
                if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(new((int)(100d * i / archives.Count), $"Распаковка {Path.GetFileName(path)}…"));
                    convertedArchive = ConvertToZip(path);
                    installPath = convertedArchive;
                }
            var start = (int)(100d * i / archives.Count);
            var end = (int)(100d * (i + 1) / archives.Count);
            InstallZip(gameRoot, installPath, Path.GetFileNameWithoutExtension(path), progress, start, end - start);
            installed.Add(Path.GetFileName(path));
            }
            finally
            {
                try { if (convertedArchive is not null && File.Exists(convertedArchive)) File.Delete(convertedArchive); } catch { }
            }
        }
        progress?.Report(new(100, "Локальные компоненты установлены"));
        return new ComponentInstallResult(installed);
    }

    internal void InstallZip(string gameRoot, string archivePath, string componentName,
        IProgress<InstallProgress>? progress = null, int basePercent = 0, int progressRange = 100)
    {
        var stateRoot = Path.Combine(gameRoot, StateDirectoryName);
        var safeName = string.Concat(componentName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var backupRoot = Path.Combine(stateRoot, "backups", DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + "-" + safeName);
        var record = new ComponentInstallRecord { Component = componentName, InstalledAt = DateTimeOffset.Now };

        using var archive = ZipFile.OpenRead(archivePath);
        var fileEntries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToArray();
        if (fileEntries.Length == 0) throw new InstallerException($"Архив {Path.GetFileName(archivePath)} не содержит файлов.");
        var prefix = FindWrapperPrefix(fileEntries.Select(e => e.FullName));

        try
        {
            for (var i = 0; i < fileEntries.Length; i++)
            {
                var entry = fileEntries[i];
                var relative = NormalizeEntry(entry.FullName, prefix);
                if (string.IsNullOrWhiteSpace(relative)) continue;
                var destination = SafeCombine(gameRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                string? backupRelative = null;
                if (File.Exists(destination))
                {
                    backupRelative = Path.Combine("backups", Path.GetFileName(backupRoot), relative);
                    var backup = SafeCombine(stateRoot, backupRelative);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(destination, backup, true);
                }

                var temporary = destination + ".component-installer.tmp";
                using (var source = entry.Open())
                using (var target = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    source.CopyTo(target);
                File.Move(temporary, destination, true);
                record.Files.Add(new ComponentFileRecord
                {
                    RelativePath = relative,
                    Sha256 = ComputeSha256(destination),
                    BackupRelativePath = backupRelative
                });
                progress?.Report(new(Math.Min(99, basePercent + (int)(progressRange * (i + 1d) / fileEntries.Length)),
                    $"{componentName}: {relative}"));
            }

            Directory.CreateDirectory(Path.Combine(stateRoot, "records"));
            var recordPath = Path.Combine(stateRoot, "records", DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + "-" + safeName + ".json");
            File.WriteAllText(recordPath, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            Rollback(gameRoot, stateRoot, record);
            throw;
        }
    }

    private static async Task<GitHubRelease> GetLatestReleaseAsync(HttpClient client, string repository, CancellationToken token)
    {
        using var response = await client.GetAsync($"https://api.github.com/repos/{repository}/releases/latest", token);
        if ((int)response.StatusCode == 403)
            throw new InstallerException("GitHub временно ограничил количество запросов. Подождите и повторите установку.");
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "latest";
        var assets = root.GetProperty("assets").EnumerateArray().Select(a => new GitHubAsset(
            a.GetProperty("name").GetString() ?? "",
            a.GetProperty("browser_download_url").GetString() ?? "")).ToArray();
        return new GitHubRelease(tag, assets);
    }

    private static async Task DownloadAsync(HttpClient client, string url, string destination, string name,
        int basePercent, int range, IProgress<InstallProgress>? progress, CancellationToken token)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var length = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920];
        long total = 0;
        var lastReportedPercent = -1;
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            total += read;
            var downloaded = length is > 0 ? (int)(range * total / length.Value) : 1;
            var percent = Math.Min(99, basePercent + downloaded);
            if (percent != lastReportedPercent)
            {
                lastReportedPercent = percent;
                progress?.Report(new(percent, $"Загрузка {name}: {total / 1024d / 1024d:F1} МБ"));
            }
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CyberpunkVRPortInstaller", InstallerEngine.ModVersion));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static string? FindWrapperPrefix(IEnumerable<string> entryNames)
    {
        var paths = entryNames.Select(p => p.Replace('\\', '/').TrimStart('/')).Where(p => p.Contains('/')).ToArray();
        if (paths.Length == 0) return null;
        var firstParts = paths.Select(p => p.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (firstParts.Length != 1) return null;
        var first = firstParts[0];
        string[] gameRoots = ["archive", "bin", "engine", "mods", "r6", "red4ext", "tools"];
        if (gameRoots.Contains(first, StringComparer.OrdinalIgnoreCase)) return null;
        var prefix = first + "/";
        var containsGameRoot = paths.Any(p => gameRoots.Any(r => p.StartsWith(prefix + r + "/", StringComparison.OrdinalIgnoreCase)));
        return containsGameRoot ? prefix : null;
    }

    private static string NormalizeEntry(string entryName, string? prefix)
    {
        var normalized = entryName.Replace('\\', '/').TrimStart('/');
        if (prefix is not null && normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized[prefix.Length..];
        var relative = normalized.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar).Any(p => p == ".."))
            throw new InstallerException($"Недопустимый путь в архиве: {entryName}");
        return relative;
    }

    private static bool IsSupportedArchive(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);

    private static string ConvertToZip(string archivePath)
    {
        var toolRoot = Path.Combine(Path.GetTempPath(), "CyberpunkVRInstaller-7zip-" + Guid.NewGuid().ToString("N"));
        var extractRoot = Path.Combine(toolRoot, "extracted");
        var zipPath = Path.Combine(Path.GetTempPath(), "CyberpunkVRInstaller-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            Directory.CreateDirectory(extractRoot);
            var executable = Path.Combine(toolRoot, "7z.exe");
            ExtractResource("SevenZip.exe", executable);
            ExtractResource("SevenZip.dll", Path.Combine(toolRoot, "7z.dll"));

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("x");
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-bd");
            startInfo.ArgumentList.Add("-o" + extractRoot);
            startInfo.ArgumentList.Add(archivePath);
            using var process = Process.Start(startInfo) ?? throw new InstallerException("Не удалось запустить встроенный распаковщик 7-Zip.");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InstallerException($"7-Zip не смог распаковать {Path.GetFileName(archivePath)} (код {process.ExitCode}).\n{error}\n{output}");
            if (!Directory.EnumerateFiles(extractRoot, "*", SearchOption.AllDirectories).Any())
                throw new InstallerException($"Архив {Path.GetFileName(archivePath)} не содержит файлов.");
            ZipFile.CreateFromDirectory(extractRoot, zipPath, CompressionLevel.NoCompression, false);
            return zipPath;
        }
        catch
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            throw;
        }
        finally
        {
            try { if (Directory.Exists(toolRoot)) Directory.Delete(toolRoot, true); } catch { }
        }
    }

    private static void ExtractResource(string resourceName, string destination)
    {
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InstallerException($"В инсталляторе отсутствует ресурс {resourceName}.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(target);
    }

    private static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relative));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InstallerException($"Путь выходит за пределы папки игры: {relative}");
        return fullPath;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void Rollback(string gameRoot, string stateRoot, ComponentInstallRecord record)
    {
        foreach (var file in record.Files.AsEnumerable().Reverse())
        {
            var destination = SafeCombine(gameRoot, file.RelativePath);
            try
            {
                if (File.Exists(destination)) File.Delete(destination);
                if (file.BackupRelativePath is not null)
                {
                    var backup = SafeCombine(stateRoot, file.BackupRelativePath);
                    if (File.Exists(backup))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        File.Copy(backup, destination, true);
                    }
                }
            }
            catch { }
        }
    }

    private static void EnsureGameRoot(string gameRoot)
    {
        if (!GameLocator.IsGameRoot(gameRoot))
            throw new InstallerException("Сначала выберите корректную папку Cyberpunk 2077.");
        if (Process.GetProcessesByName("Cyberpunk2077").Length > 0)
            throw new InstallerException("Cyberpunk 2077 запущен. Закройте игру перед установкой компонентов.");
    }

    private sealed record GitHubRelease(string TagName, IReadOnlyList<GitHubAsset> Assets);
    private sealed record GitHubAsset(string Name, string DownloadUrl);
}

internal sealed record ComponentInstallResult(IReadOnlyList<string> InstalledComponents);

internal sealed class ComponentInstallRecord
{
    public string Component { get; set; } = "";
    public DateTimeOffset InstalledAt { get; set; }
    public List<ComponentFileRecord> Files { get; set; } = [];
}

internal sealed class ComponentFileRecord
{
    public string RelativePath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string? BackupRelativePath { get; set; }
}
