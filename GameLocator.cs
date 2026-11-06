using Microsoft.Win32;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CyberpunkVRInstaller;

internal static partial class GameLocator
{
    public static IReadOnlyList<string> FindInstallations()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddRegistryPath(candidates, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\GOG.com\Games\1423049311", "path");
        AddRegistryPath(candidates, Registry.LocalMachine, @"SOFTWARE\GOG.com\Games\1423049311", "path");
        FindFromUninstallRegistry(candidates, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        FindFromUninstallRegistry(candidates, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
        FindSteam(candidates);
        FindEpic(candidates);

        string[] common =
        [
            @"C:\Program Files (x86)\Steam\steamapps\common\Cyberpunk 2077",
            @"C:\Program Files\Steam\steamapps\common\Cyberpunk 2077",
            @"C:\Program Files (x86)\GOG Galaxy\Games\Cyberpunk 2077",
            @"C:\GOG Games\Cyberpunk 2077",
            @"C:\Program Files\Epic Games\Cyberpunk2077"
        ];
        foreach (var path in common) candidates.Add(path);

        return candidates.Where(IsGameRoot).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool IsGameRoot(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        File.Exists(Path.Combine(path, "bin", "x64", "Cyberpunk2077.exe"));

    private static void AddRegistryPath(HashSet<string> result, RegistryKey hive, string keyPath, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey(keyPath);
            if (key?.GetValue(valueName) is string path) result.Add(path);
        }
        catch { }
    }

    private static void FindFromUninstallRegistry(HashSet<string> result, RegistryKey hive, string keyPath)
    {
        try
        {
            using var root = hive.OpenSubKey(keyPath);
            if (root is null) return;
            foreach (var childName in root.GetSubKeyNames())
            {
                using var child = root.OpenSubKey(childName);
                var displayName = child?.GetValue("DisplayName") as string;
                if (displayName?.Contains("Cyberpunk 2077", StringComparison.OrdinalIgnoreCase) == true &&
                    child?.GetValue("InstallLocation") is string path && !string.IsNullOrWhiteSpace(path))
                    result.Add(path);
            }
        }
        catch { }
    }

    private static void FindSteam(HashSet<string> result)
    {
        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string path) steamRoots.Add(path.Replace('/', '\\'));
        }
        catch { }

        foreach (var defaultRoot in new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" })
            steamRoots.Add(defaultRoot);

        foreach (var root in steamRoots.ToArray())
        {
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            try
            {
                foreach (Match match in SteamPathRegex().Matches(File.ReadAllText(vdf)))
                    steamRoots.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
            }
            catch { }
        }

        foreach (var root in steamRoots)
            result.Add(Path.Combine(root, "steamapps", "common", "Cyberpunk 2077"));
    }

    private static void FindEpic(HashSet<string> result)
    {
        var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(manifests, "*.item"))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                var name = root.TryGetProperty("DisplayName", out var display) ? display.GetString() : null;
                if (name?.Contains("Cyberpunk 2077", StringComparison.OrdinalIgnoreCase) == true &&
                    root.TryGetProperty("InstallLocation", out var install) && install.GetString() is string path)
                    result.Add(path);
            }
        }
        catch { }
    }

    [GeneratedRegex("\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex SteamPathRegex();
}

