using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace CyberpunkVRInstaller;

internal sealed class InstallerEngine
{
    public const string ModVersion = "0.2.2";
    public const string InstallerVersion = ModVersion;
    private const string PayloadResource = "CyberpunkVRPort.Payload.zip";
    private const string StateDirectoryName = ".cyberpunk-vr-installer";
    private const string ManifestFileName = "install-manifest.json";

    private static readonly string[] ObsoletePaths =
    [
        @"red4ext\plugins\CyberpunkVR_Stereo",
        @"red4ext\plugins\CyberpunkVR_Hands",
        @"r6\scripts\CyberpunkVRPort_LootUi",
        @"r6\scripts\CyberpunkVRPort_SettingsGuard",
        @"r6\scripts\CyberpunkVRPort_Cyberware\vrport_launcher_tweak.reds",
        @"r6\tweaks\vrport",
        @"r6\tweaks\vrcigarette",
        @"archive\pc\mod\cyberpunkvrport.archive",
        @"archive\pc\mod\cyberpunkvrport.archive.xl",
        @"engine\config\platform\pc\vrcam_cpu_tweaks.ini",
        @"archive\pc\mod\VRCigarette.archive.xl",
        @"archive\pc\mod\vrport_basketball.archive",
        @"archive\pc\mod\vrport_mag.archive",
        @"archive\pc\mod\vrport_menu_native.archive",
        @"archive\pc\mod\vrport_menu_panels.archive"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public bool IsInstalled(string gameRoot) => File.Exists(GetManifestPath(gameRoot));

    public bool IsDisabled(string gameRoot)
    {
        try { return LoadManifest(gameRoot)?.IsDisabled == true; }
        catch { return false; }
    }

    public string? GetInstalledVersion(string gameRoot)
    {
        try { return LoadManifest(gameRoot)?.InstallerVersion; }
        catch { return null; }
    }

    public ModStateResult GetModState(string gameRoot)
    {
        var manifest = LoadManifest(gameRoot);
        if (manifest is null) return new(VrModState.NotInstalled, []);
        if (manifest.IsDisabled)
        {
            var disabledProblems = VerifyDisabledState(gameRoot, manifest);
            return new(disabledProblems.Count == 0 ? VrModState.Disabled : VrModState.Incomplete, disabledProblems);
        }

        var problems = manifest.Files
            .Where(file => !File.Exists(SafeCombine(gameRoot, file.RelativePath)))
            .Select(file => L.T($"Не найден активный файл: {file.RelativePath}", $"Active file not found: {file.RelativePath}"))
            .ToArray();
        return new(problems.Length == 0 ? VrModState.Enabled : VrModState.Incomplete, problems);
    }

    public ModStateResult Disable(string gameRoot, IProgress<InstallProgress>? progress = null)
    {
        EnsureSafeToModify(gameRoot);
        var manifest = LoadManifest(gameRoot) ?? throw new InstallerException(L.T(
            "VR-мод не установлен этим инсталлятором.", "The VR mod was not installed by this installer."));
        if (manifest.IsDisabled) return GetModState(gameRoot);

        var stateRoot = Path.Combine(gameRoot, StateDirectoryName);
        var disabledRoot = Path.Combine(stateRoot, "disabled");
        var moved = new List<string>();
        try
        {
            for (var i = 0; i < manifest.Files.Count; i++)
            {
                var file = manifest.Files[i];
                var activePath = SafeCombine(gameRoot, file.RelativePath);
                if (!File.Exists(activePath)) continue;

                var disabledPath = SafeCombine(disabledRoot, file.RelativePath);
                if (File.Exists(disabledPath))
                    throw new InstallerException(L.T($"Хранилище отключённых файлов уже содержит: {file.RelativePath}",
                        $"Disabled-file storage already contains: {file.RelativePath}"));
                Directory.CreateDirectory(Path.GetDirectoryName(disabledPath)!);
                File.Move(activePath, disabledPath);
                moved.Add(file.RelativePath);

                var backup = FindOriginalBackup(manifest, file.RelativePath);
                if (backup is not null)
                {
                    var backupPath = SafeCombine(stateRoot, backup.BackupRelativePath);
                    if (File.Exists(backupPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(activePath)!);
                        File.Copy(backupPath, activePath, true);
                    }
                }
                DeleteEmptyParents(activePath, gameRoot);
                progress?.Report(new((int)(90d * (i + 1) / Math.Max(1, manifest.Files.Count)),
                    L.T($"Отключение: {file.RelativePath}", $"Disabling: {file.RelativePath}")));
            }

            manifest.IsDisabled = true;
            manifest.DisabledAt = DateTimeOffset.Now;
            manifest.DisabledFiles = moved;
            var problems = VerifyDisabledState(gameRoot, manifest);
            if (problems.Count > 0)
                throw new InstallerException(L.T("Проверка отключения не пройдена:\n• ",
                    "Disable verification failed:\n• ") + string.Join("\n• ", problems));
            SaveManifest(gameRoot, manifest);
            progress?.Report(new(100, L.T($"VR-мод отключён; проверено файлов: {moved.Count}",
                $"VR mod disabled; files verified: {moved.Count}")));
            return new(VrModState.Disabled, []);
        }
        catch
        {
            RollbackDisable(gameRoot, stateRoot, disabledRoot, manifest, moved);
            throw;
        }
    }

    public ModStateResult Enable(string gameRoot, IProgress<InstallProgress>? progress = null)
    {
        EnsureSafeToModify(gameRoot);
        var manifest = LoadManifest(gameRoot) ?? throw new InstallerException(L.T(
            "VR-мод не установлен этим инсталлятором.", "The VR mod was not installed by this installer."));
        if (!manifest.IsDisabled) return GetModState(gameRoot);

        var stateRoot = Path.Combine(gameRoot, StateDirectoryName);
        var disabledRoot = Path.Combine(stateRoot, "disabled");
        var restored = new List<string>();
        try
        {
            for (var i = 0; i < manifest.DisabledFiles.Count; i++)
            {
                var relative = manifest.DisabledFiles[i];
                var disabledPath = SafeCombine(disabledRoot, relative);
                if (!File.Exists(disabledPath))
                    throw new InstallerException(L.T($"Не найден сохранённый файл VR-мода: {relative}",
                        $"Stored VR mod file not found: {relative}"));
                var activePath = SafeCombine(gameRoot, relative);
                if (File.Exists(activePath))
                {
                    var backup = FindOriginalBackup(manifest, relative);
                    if (backup is null)
                        throw new InstallerException(L.T($"Невозможно включить мод: путь уже занят: {relative}",
                            $"Cannot enable the mod; path is already occupied: {relative}"));
                    var backupPath = SafeCombine(stateRoot, backup.BackupRelativePath);
                    if (!File.Exists(backupPath) || !string.Equals(ComputeSha256(activePath), ComputeSha256(backupPath), StringComparison.OrdinalIgnoreCase))
                        throw new InstallerException(L.T($"Файл был изменён, пока мод отключён: {relative}",
                            $"File was changed while the mod was disabled: {relative}"));
                    File.Delete(activePath);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(activePath)!);
                File.Move(disabledPath, activePath);
                restored.Add(relative);
                progress?.Report(new((int)(90d * (i + 1) / Math.Max(1, manifest.DisabledFiles.Count)),
                    L.T($"Включение: {relative}", $"Enabling: {relative}")));
            }

            var missing = restored.Where(relative => !File.Exists(SafeCombine(gameRoot, relative))).ToArray();
            if (missing.Length > 0)
                throw new InstallerException(L.T("Проверка включения не пройдена:\n• ",
                    "Enable verification failed:\n• ") + string.Join("\n• ", missing));
            manifest.IsDisabled = false;
            manifest.DisabledAt = null;
            manifest.DisabledFiles.Clear();
            SaveManifest(gameRoot, manifest);
            try { if (Directory.Exists(disabledRoot) && !Directory.EnumerateFileSystemEntries(disabledRoot).Any()) Directory.Delete(disabledRoot, true); } catch { }
            progress?.Report(new(100, L.T($"VR-мод включён; восстановлено файлов: {restored.Count}",
                $"VR mod enabled; files restored: {restored.Count}")));
            return GetModState(gameRoot);
        }
        catch
        {
            RollbackEnable(gameRoot, stateRoot, disabledRoot, manifest, restored);
            throw;
        }
    }

    public void Install(string gameRoot, IProgress<InstallProgress>? progress = null)
    {
        EnsureSafeToModify(gameRoot);
        if (IsInstalled(gameRoot))
        {
            progress?.Report(new(2, L.T("Удаление предыдущей установки…", "Removing previous installation…")));
            Uninstall(gameRoot, progress);
        }

        var stateRoot = Path.Combine(gameRoot, StateDirectoryName);
        var backupName = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupRoot = Path.Combine(stateRoot, "backups", backupName);
        Directory.CreateDirectory(backupRoot);

        var manifest = new InstallManifest();
        try
        {
            BackupAndRemoveObsolete(gameRoot, backupRoot, manifest, progress);
            using var payload = OpenPayload();
            using var archive = new ZipArchive(payload, ZipArchiveMode.Read);
            var fileEntries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToArray();

            for (var index = 0; index < fileEntries.Length; index++)
            {
                var entry = fileEntries[index];
                var relative = NormalizeEntry(entry.FullName);
                var destination = SafeCombine(gameRoot, relative);
                var existedBefore = File.Exists(destination);

                if (existedBefore)
                {
                    var backupRelative = Path.Combine("overwritten", relative);
                    var backupPath = Path.Combine(backupRoot, backupRelative);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    File.Copy(destination, backupPath, true);
                    manifest.Backups.Add(new BackupItem
                    {
                        RelativePath = relative,
                        BackupRelativePath = Path.Combine("backups", backupName, backupRelative),
                        IsDirectory = false,
                        IsObsoleteItem = false
                    });
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var temporary = destination + ".vrinstaller.tmp";
                using (var source = entry.Open())
                using (var target = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    source.CopyTo(target);
                File.Move(temporary, destination, true);

                manifest.Files.Add(new InstalledFile
                {
                    RelativePath = relative,
                    Sha256 = ComputeSha256(destination),
                    ExistedBefore = existedBefore
                });
                progress?.Report(new(8 + (int)(88d * (index + 1) / fileEntries.Length),
                    L.T($"Установка: {relative}", $"Installing: {relative}")));
            }

            Directory.CreateDirectory(stateRoot);
            var manifestPath = GetManifestPath(gameRoot);
            var manifestTemp = manifestPath + ".tmp";
            File.WriteAllText(manifestTemp, JsonSerializer.Serialize(manifest, JsonOptions));
            File.Move(manifestTemp, manifestPath, true);
            progress?.Report(new(100, L.T($"Cyberpunk VR Port {ModVersion} установлен",
                $"Cyberpunk VR Port {ModVersion} installed")));
        }
        catch
        {
            RollbackFailedInstall(gameRoot, manifest);
            throw;
        }
    }

    public UninstallResult Uninstall(string gameRoot, IProgress<InstallProgress>? progress = null)
    {
        EnsureSafeToModify(gameRoot);
        var manifest = LoadManifest(gameRoot) ?? throw new InstallerException(L.T(
            "Манифест установки не найден.", "Installation manifest not found."));
        if (manifest.IsDisabled)
        {
            progress?.Report(new(1, L.T("Подготовка отключённого мода к удалению…",
                "Preparing the disabled mod for removal…")));
            Enable(gameRoot, progress);
            manifest = LoadManifest(gameRoot) ?? throw new InstallerException(L.T(
                "Манифест установки не найден после включения.", "Installation manifest not found after enabling the mod."));
        }
        var stateRoot = Path.Combine(gameRoot, StateDirectoryName);
        var preserved = new List<string>();

        for (var i = manifest.Files.Count - 1; i >= 0; i--)
        {
            var file = manifest.Files[i];
            var path = SafeCombine(gameRoot, file.RelativePath);
            if (File.Exists(path))
            {
                if (string.Equals(ComputeSha256(path), file.Sha256, StringComparison.OrdinalIgnoreCase))
                    File.Delete(path);
                else
                {
                    preserved.Add(file.RelativePath);
                    continue;
                }
            }

            var backup = manifest.Backups.FirstOrDefault(b => !b.IsObsoleteItem &&
                string.Equals(b.RelativePath, file.RelativePath, StringComparison.OrdinalIgnoreCase));
            if (backup is not null)
                RestoreBackup(gameRoot, stateRoot, backup);

            progress?.Report(new((int)(80d * (manifest.Files.Count - i) / Math.Max(1, manifest.Files.Count)),
                L.T($"Удаление: {file.RelativePath}", $"Removing: {file.RelativePath}")));
            DeleteEmptyParents(path, gameRoot);
        }

        foreach (var backup in manifest.Backups.Where(b => b.IsObsoleteItem))
            RestoreBackup(gameRoot, stateRoot, backup);

        if (preserved.Count == 0)
        {
            try { Directory.Delete(stateRoot, true); } catch { }
        }
        else
        {
            manifest.Files = manifest.Files.Where(f => preserved.Contains(f.RelativePath, StringComparer.OrdinalIgnoreCase)).ToList();
            File.WriteAllText(GetManifestPath(gameRoot), JsonSerializer.Serialize(manifest, JsonOptions));
        }

        progress?.Report(new(100, preserved.Count == 0 ? L.T("Мод удалён", "Mod removed") :
            L.T("Мод удалён; изменённые файлы сохранены", "Mod removed; modified files preserved")));
        return new UninstallResult(preserved);
    }

    public IReadOnlyList<string> FindConflicts(string gameRoot)
    {
        var result = new List<string>();
        var dxgi = Path.Combine(gameRoot, "bin", "x64", "dxgi.dll");
        if (File.Exists(dxgi)) result.Add(L.T(@"bin\x64\dxgi.dll — возможен конфликт с R.E.A.L. VR или другим прокси",
            @"bin\x64\dxgi.dll — possible conflict with R.E.A.L. VR or another proxy"));

        var pluginDir = Path.Combine(gameRoot, "red4ext", "plugins", "CyberpunkVR_Stereo");
        if (Directory.Exists(pluginDir))
            result.Add(L.T(@"red4ext\plugins\CyberpunkVR_Stereo — осталась старая версия VR Port 0.1.9",
                @"red4ext\plugins\CyberpunkVR_Stereo — old VR Port 0.1.9 files remain"));
        return result;
    }

    private static void EnsureSafeToModify(string gameRoot)
    {
        if (!GameLocator.IsGameRoot(gameRoot))
            throw new InstallerException(L.T("Выбрана не корневая папка Cyberpunk 2077 (не найден bin\\x64\\Cyberpunk2077.exe).",
                "The selected folder is not the Cyberpunk 2077 root (bin\\x64\\Cyberpunk2077.exe was not found)."));
        if (Process.GetProcessesByName("Cyberpunk2077").Length > 0)
            throw new InstallerException(L.T("Cyberpunk 2077 запущен. Закройте игру и повторите попытку.",
                "Cyberpunk 2077 is running. Close the game and try again."));
    }

    private static Stream OpenPayload() =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource)
        ?? throw new InstallerException(L.T("В инсталляторе отсутствует архив VR-порта.",
            "The installer does not contain the VR port archive."));

    private static string NormalizeEntry(string entryName)
    {
        var relative = entryName.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar).Any(p => p == ".."))
            throw new InstallerException(L.T($"Недопустимый путь в архиве: {entryName}", $"Invalid archive path: {entryName}"));
        return relative;
    }

    private static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relative));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InstallerException(L.T($"Путь выходит за пределы папки игры: {relative}",
                $"Path escapes the game folder: {relative}"));
        return fullPath;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string GetManifestPath(string gameRoot) => Path.Combine(gameRoot, StateDirectoryName, ManifestFileName);

    private static InstallManifest? LoadManifest(string gameRoot)
    {
        var path = GetManifestPath(gameRoot);
        return File.Exists(path) ? JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(path), JsonOptions) : null;
    }

    private static void SaveManifest(string gameRoot, InstallManifest manifest)
    {
        var path = GetManifestPath(gameRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(manifest, JsonOptions));
        File.Move(temporary, path, true);
    }

    private static BackupItem? FindOriginalBackup(InstallManifest manifest, string relativePath) =>
        manifest.Backups.FirstOrDefault(backup => !backup.IsObsoleteItem &&
            string.Equals(backup.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

    private static List<string> VerifyDisabledState(string gameRoot, InstallManifest manifest)
    {
        var problems = new List<string>();
        var stateRoot = Path.Combine(gameRoot, StateDirectoryName);
        var disabledRoot = Path.Combine(stateRoot, "disabled");
        foreach (var relative in manifest.DisabledFiles)
        {
            var stored = SafeCombine(disabledRoot, relative);
            if (!File.Exists(stored)) problems.Add(L.T($"Нет файла в защищённом хранилище: {relative}",
                $"File is missing from protected storage: {relative}"));
            var active = SafeCombine(gameRoot, relative);
            var backup = FindOriginalBackup(manifest, relative);
            if (backup is null)
            {
                if (File.Exists(active)) problems.Add(L.T($"Файл VR-мода всё ещё активен: {relative}",
                    $"VR mod file is still active: {relative}"));
            }
            else
            {
                var original = SafeCombine(stateRoot, backup.BackupRelativePath);
                if (!File.Exists(active) || !File.Exists(original) ||
                    !string.Equals(ComputeSha256(active), ComputeSha256(original), StringComparison.OrdinalIgnoreCase))
                    problems.Add(L.T($"Исходный файл не восстановлен: {relative}",
                        $"Original file was not restored: {relative}"));
            }
        }
        return problems;
    }

    private static void RollbackDisable(string gameRoot, string stateRoot, string disabledRoot,
        InstallManifest manifest, IEnumerable<string> moved)
    {
        foreach (var relative in moved.Reverse())
        {
            try
            {
                var active = SafeCombine(gameRoot, relative);
                var disabled = SafeCombine(disabledRoot, relative);
                var backup = FindOriginalBackup(manifest, relative);
                if (backup is not null && File.Exists(active)) File.Delete(active);
                if (File.Exists(disabled))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(active)!);
                    File.Move(disabled, active, true);
                }
            }
            catch { }
        }
    }

    private static void RollbackEnable(string gameRoot, string stateRoot, string disabledRoot,
        InstallManifest manifest, IEnumerable<string> restored)
    {
        foreach (var relative in restored.Reverse())
        {
            try
            {
                var active = SafeCombine(gameRoot, relative);
                var disabled = SafeCombine(disabledRoot, relative);
                if (File.Exists(active))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(disabled)!);
                    File.Move(active, disabled, true);
                }
                var backup = FindOriginalBackup(manifest, relative);
                if (backup is not null)
                {
                    var original = SafeCombine(stateRoot, backup.BackupRelativePath);
                    if (File.Exists(original)) File.Copy(original, active, true);
                }
            }
            catch { }
        }
    }

    private static void BackupAndRemoveObsolete(string gameRoot, string backupRoot, InstallManifest manifest, IProgress<InstallProgress>? progress)
    {
        for (var i = 0; i < ObsoletePaths.Length; i++)
        {
            var relative = ObsoletePaths[i];
            var source = SafeCombine(gameRoot, relative);
            var isDirectory = Directory.Exists(source);
            if (!isDirectory && !File.Exists(source)) continue;

            var backupRelative = Path.Combine("obsolete", relative);
            var destination = Path.Combine(backupRoot, backupRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (isDirectory)
            {
                CopyDirectory(source, destination);
                Directory.Delete(source, true);
            }
            else
            {
                File.Copy(source, destination, true);
                File.Delete(source);
            }
            var stateRoot = Path.GetDirectoryName(Path.GetDirectoryName(backupRoot)!)!;
            manifest.Backups.Add(new BackupItem
            {
                RelativePath = relative,
                BackupRelativePath = Path.GetRelativePath(stateRoot, destination),
                IsDirectory = isDirectory,
                IsObsoleteItem = true
            });
            progress?.Report(new(3 + i / 2, L.T($"Архивация старого компонента: {relative}",
                $"Archiving old component: {relative}")));
        }
    }

    private static void RestoreBackup(string gameRoot, string stateRoot, BackupItem backup)
    {
        var source = SafeCombine(stateRoot, backup.BackupRelativePath);
        var destination = SafeCombine(gameRoot, backup.RelativePath);
        if (backup.IsDirectory)
        {
            if (!Directory.Exists(destination) && Directory.Exists(source)) CopyDirectory(source, destination);
        }
        else if (!File.Exists(destination) && File.Exists(source))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, true);
        }
    }

    private static void RollbackFailedInstall(string gameRoot, InstallManifest manifest)
    {
        var stateRoot = Path.Combine(gameRoot, StateDirectoryName);
        foreach (var file in manifest.Files.AsEnumerable().Reverse())
        {
            var path = SafeCombine(gameRoot, file.RelativePath);
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
        foreach (var backup in manifest.Backups.AsEnumerable().Reverse())
        {
            try { RestoreBackup(gameRoot, stateRoot, backup); } catch { }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static void DeleteEmptyParents(string filePath, string stopAt)
    {
        var current = Path.GetDirectoryName(filePath);
        var stop = Path.GetFullPath(stopAt).TrimEnd(Path.DirectorySeparatorChar);
        while (!string.IsNullOrEmpty(current) && !string.Equals(current, stop, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.EnumerateFileSystemEntries(current).Any()) break;
                Directory.Delete(current);
                current = Path.GetDirectoryName(current);
            }
            catch { break; }
        }
    }
}

internal sealed record UninstallResult(IReadOnlyList<string> PreservedModifiedFiles);

