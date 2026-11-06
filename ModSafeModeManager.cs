using System.Diagnostics;
using System.Text.Json;

namespace CyberpunkVRInstaller;

internal sealed class ModSafeModeManager
{
    private const string StateDirectoryName = ".cyberpunk-mod-safe-mode";
    private const string ManifestName = "safe-mode.json";

    private static readonly string[] ModLoadPaths =
    [
        @"archive\pc\mod",
        @"mods",
        @"r6\scripts",
        @"r6\tweaks",
        @"r6\input",
        @"red4ext\plugins",
        // This directory also contains standalone ASI plugins such as vpx2077.asi.
        // Moving only CET left other native injectors active in the old Safe mode.
        @"bin\x64\plugins",
        @"bin\x64\winmm.dll",
        @"bin\x64\version.dll",
        @"bin\x64\dinput8.dll",
        @"bin\x64\winhttp.dll",
        @"bin\x64\dxgi.dll",
        @"bin\x64\d3d11.dll",
        @"bin\x64\openvr_api.dll"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SafeModeResult GetStatus(string gameRoot)
    {
        var manifest = Load(gameRoot);
        if (manifest is null) return new(false, true, []);
        var problems = VerifyActive(gameRoot, manifest);
        return new(true, problems.Count == 0, problems);
    }

    public SafeModeResult Activate(string gameRoot, IProgress<InstallProgress>? progress = null)
    {
        EnsureSafe(gameRoot);
        var existing = Load(gameRoot);
        if (existing is not null) return Repair(gameRoot, progress);

        var stateRoot = Path.Combine(gameRoot, StateDirectoryName);
        var payloadRoot = Path.Combine(stateRoot, "payload");
        var manifest = new SafeModeManifest { ActivatedAt = DateTimeOffset.Now };
        try
        {
            for (var i = 0; i < ModLoadPaths.Length; i++)
            {
                var relative = ModLoadPaths[i];
                var source = SafeCombine(gameRoot, relative);
                var isDirectory = Directory.Exists(source);
                if (!isDirectory && !File.Exists(source)) continue;
                var destination = SafeCombine(payloadRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (isDirectory) Directory.Move(source, destination);
                else File.Move(source, destination);
                manifest.Items.Add(new SafeModeItem { RelativePath = relative, IsDirectory = isDirectory });
                progress?.Report(new((int)(90d * (i + 1) / ModLoadPaths.Length), $"Safe mode: {relative}"));
            }

            var problems = VerifyActive(gameRoot, manifest);
            if (problems.Count > 0)
                throw new InstallerException("Не удалось полностью отключить моды:\n• " + string.Join("\n• ", problems));
            Directory.CreateDirectory(stateRoot);
            File.WriteAllText(Path.Combine(stateRoot, ManifestName), JsonSerializer.Serialize(manifest, JsonOptions));
            progress?.Report(new(100, $"Safe mode включён; отключено объектов: {manifest.Items.Count}"));
            return new(true, true, []);
        }
        catch
        {
            RestoreItems(gameRoot, payloadRoot, manifest.Items.AsEnumerable().Reverse(), false);
            throw;
        }
    }

    public SafeModeResult Repair(string gameRoot, IProgress<InstallProgress>? progress = null)
    {
        EnsureSafe(gameRoot);
        var manifest = Load(gameRoot) ?? throw new InstallerException("Safe mode не включён.");
        var stateRoot = Path.Combine(gameRoot, StateDirectoryName);
        var payloadRoot = Path.Combine(stateRoot, "payload");

        for (var i = 0; i < ModLoadPaths.Length; i++)
        {
            var relative = ModLoadPaths[i];
            var source = SafeCombine(gameRoot, relative);
            var isDirectory = Directory.Exists(source);
            if (!isDirectory && !File.Exists(source)) continue;

            var recorded = manifest.Items.FirstOrDefault(item =>
                item.RelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase));
            if (recorded is null)
            {
                if (isDirectory)
                {
                    var prefix = relative.TrimEnd('\\') + "\\";
                    manifest.Items.RemoveAll(item => item.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                }
                manifest.Items.Add(new SafeModeItem { RelativePath = relative, IsDirectory = isDirectory });
                Directory.CreateDirectory(stateRoot);
                File.WriteAllText(Path.Combine(stateRoot, ManifestName), JsonSerializer.Serialize(manifest, JsonOptions));
            }
            else if (recorded.IsDirectory != isDirectory)
            {
                throw new InstallerException($"Тип пути Safe mode изменился: {relative}");
            }

            var destination = SafeCombine(payloadRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (isDirectory) MoveDirectoryMerge(source, destination);
            else
            {
                if (File.Exists(destination))
                    throw new InstallerException($"В хранилище Safe mode уже есть файл: {relative}");
                File.Move(source, destination);
            }
            progress?.Report(new((int)(90d * (i + 1) / ModLoadPaths.Length), $"Ремонт Safe mode: {relative}"));
        }

        var problems = VerifyActive(gameRoot, manifest);
        if (problems.Count > 0)
            throw new InstallerException("Safe mode всё ещё неполон:\n• " + string.Join("\n• ", problems));
        File.WriteAllText(Path.Combine(stateRoot, ManifestName), JsonSerializer.Serialize(manifest, JsonOptions));
        progress?.Report(new(100, $"Safe mode отремонтирован; защищено объектов: {manifest.Items.Count}"));
        return new(true, true, []);
    }

    public SafeModeResult Deactivate(string gameRoot, IProgress<InstallProgress>? progress = null)
    {
        EnsureSafe(gameRoot);
        var manifest = Load(gameRoot);
        if (manifest is null) return new(false, true, []);
        var stateRoot = Path.Combine(gameRoot, StateDirectoryName);
        var payloadRoot = Path.Combine(stateRoot, "payload");

        foreach (var item in manifest.Items)
        {
            var target = SafeCombine(gameRoot, item.RelativePath);
            if (File.Exists(target) || Directory.Exists(target))
                throw new InstallerException($"Нельзя выйти из Safe mode: путь уже занят: {item.RelativePath}");
        }

        var restored = new List<SafeModeItem>();
        try
        {
            for (var i = 0; i < manifest.Items.Count; i++)
            {
                var item = manifest.Items[i];
                RestoreItems(gameRoot, payloadRoot, [item], true);
                restored.Add(item);
                progress?.Report(new((int)(90d * (i + 1) / Math.Max(1, manifest.Items.Count)), $"Восстановление: {item.RelativePath}"));
            }
            var missing = manifest.Items.Where(item =>
            {
                var path = SafeCombine(gameRoot, item.RelativePath);
                return item.IsDirectory ? !Directory.Exists(path) : !File.Exists(path);
            }).Select(item => item.RelativePath).ToArray();
            if (missing.Length > 0)
                throw new InstallerException("Проверка восстановления не пройдена:\n• " + string.Join("\n• ", missing));
            Directory.Delete(stateRoot, true);
            progress?.Report(new(100, $"Safe mode выключен; восстановлено объектов: {restored.Count}"));
            return new(false, true, []);
        }
        catch
        {
            foreach (var item in restored.AsEnumerable().Reverse())
            {
                try
                {
                    var source = SafeCombine(gameRoot, item.RelativePath);
                    var destination = SafeCombine(payloadRoot, item.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    if (item.IsDirectory && Directory.Exists(source)) Directory.Move(source, destination);
                    else if (!item.IsDirectory && File.Exists(source)) File.Move(source, destination);
                }
                catch { }
            }
            throw;
        }
    }

    private static List<string> VerifyActive(string gameRoot, SafeModeManifest manifest)
    {
        var problems = new List<string>();
        var payloadRoot = Path.Combine(gameRoot, StateDirectoryName, "payload");
        foreach (var item in manifest.Items)
        {
            var active = SafeCombine(gameRoot, item.RelativePath);
            if (File.Exists(active) || Directory.Exists(active))
                problems.Add($"Остался активным: {item.RelativePath}");
            var stored = SafeCombine(payloadRoot, item.RelativePath);
            if (item.IsDirectory ? !Directory.Exists(stored) : !File.Exists(stored))
                problems.Add($"Не найден в хранилище: {item.RelativePath}");
        }

        // Also inspect the current definition. This makes an older manifest fail closed
        // when a newer installer learns about another mod loading path.
        var recorded = manifest.Items.Select(item => item.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in ModLoadPaths.Where(path => !recorded.Contains(path)))
        {
            var active = SafeCombine(gameRoot, relative);
            if (File.Exists(active) || Directory.Exists(active))
                problems.Add($"Не отключён Safe mode: {relative}");
        }
        return problems;
    }

    private static void RestoreItems(string gameRoot, string payloadRoot, IEnumerable<SafeModeItem> items, bool requireSource)
    {
        foreach (var item in items)
        {
            var source = SafeCombine(payloadRoot, item.RelativePath);
            var destination = SafeCombine(gameRoot, item.RelativePath);
            if (item.IsDirectory)
            {
                if (!Directory.Exists(source))
                {
                    if (requireSource) throw new InstallerException($"Не найдена сохранённая папка: {item.RelativePath}");
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                Directory.Move(source, destination);
            }
            else
            {
                if (!File.Exists(source))
                {
                    if (requireSource) throw new InstallerException($"Не найден сохранённый файл: {item.RelativePath}");
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(source, destination);
            }
        }
    }

    private static void MoveDirectoryMerge(string source, string destination)
    {
        if (!Directory.Exists(destination))
        {
            Directory.Move(source, destination);
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(source).ToArray())
        {
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if (Directory.Exists(entry))
            {
                if (File.Exists(target)) throw new InstallerException($"Конфликт Safe mode: {target}");
                MoveDirectoryMerge(entry, target);
            }
            else
            {
                if (File.Exists(target) || Directory.Exists(target))
                    throw new InstallerException($"Конфликт Safe mode: {target}");
                File.Move(entry, target);
            }
        }
        Directory.Delete(source);
    }

    private static SafeModeManifest? Load(string gameRoot)
    {
        var path = Path.Combine(gameRoot, StateDirectoryName, ManifestName);
        return File.Exists(path) ? JsonSerializer.Deserialize<SafeModeManifest>(File.ReadAllText(path), JsonOptions) : null;
    }

    private static void EnsureSafe(string gameRoot)
    {
        if (!GameLocator.IsGameRoot(gameRoot)) throw new InstallerException("Выбрана неправильная папка игры.");
        if (Process.GetProcessesByName("Cyberpunk2077").Length > 0)
            throw new InstallerException("Закройте Cyberpunk 2077 перед переключением Safe mode.");
    }

    private static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relative));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InstallerException($"Путь выходит за пределы папки игры: {relative}");
        return fullPath;
    }
}

internal sealed class SafeModeManifest
{
    public DateTimeOffset ActivatedAt { get; set; }
    public List<SafeModeItem> Items { get; set; } = [];
}

internal sealed class SafeModeItem
{
    public string RelativePath { get; set; } = "";
    public bool IsDirectory { get; set; }
}

internal sealed record SafeModeResult(bool IsActive, bool Verified, IReadOnlyList<string> Problems);
