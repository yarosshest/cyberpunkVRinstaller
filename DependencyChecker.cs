using System.Diagnostics;

namespace CyberpunkVRInstaller;

internal static class DependencyChecker
{
    private sealed record Definition(string Name, string Minimum, string[] Candidates, string[] SearchTokens);

    private static readonly Definition[] Definitions =
    [
        new("RED4ext", "1.31.0", [@"red4ext\RED4ext.dll"], ["red4ext.dll"]),
        new("Cyber Engine Tweaks", "1.38.0", [@"bin\x64\plugins\cyber_engine_tweaks.asi", @"bin\x64\plugins\cyber_engine_tweaks\cyber_engine_tweaks.asi"], ["cyber_engine_tweaks.asi"]),
        new("ArchiveXL", "1.28.0", [@"red4ext\plugins\ArchiveXL\ArchiveXL.dll"], ["archivexl.dll"]),
        new("TweakXL", "1.12.0", [@"red4ext\plugins\TweakXL\TweakXL.dll"], ["tweakxl.dll"]),
        new("redscript", "0.5.32", [@"engine\tools\scc.exe"], ["scc.exe", "redscript"]),
        new("Codeware", "1.21.0", [@"red4ext\plugins\Codeware\Codeware.dll"], ["codeware.dll"]),
        new("Visual Holsters (Automatic Clothes Swap)", "required", [], ["visualholster", "automaticclothesswap", "automatic_clothes_swap"]),
        new("Equipment-EX", "required", [], ["equipmentex", "equipment-ex", "equipment_ex"]),
        new("Nova Optics", "required", [], ["novaoptics", "nova_optics", "nova-optics"]),
        new("Input Loader", "required", [@"red4ext\plugins\input_loader\input_loader.dll", @"red4ext\plugins\InputLoader\InputLoader.dll"], ["inputloader", "input_loader"]),
        new("More Occluders (Scripted v0.3)", "0.3", [], ["moreoccluders", "more_occluders", "more-occluders"])
    ];

    public static IReadOnlyList<DependencyStatus> Check(string gameRoot)
    {
        var searchableRoots = new[]
        {
            Path.Combine(gameRoot, "red4ext", "plugins"), Path.Combine(gameRoot, "archive", "pc", "mod"),
            Path.Combine(gameRoot, "r6", "scripts"), Path.Combine(gameRoot, "r6", "input"),
            Path.Combine(gameRoot, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods"),
            Path.Combine(gameRoot, "engine", "tools")
        };

        var paths = new List<string>();
        foreach (var root in searchableRoots.Where(Directory.Exists))
        {
            try
            {
                paths.AddRange(Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories));
            }
            catch { }
        }

        var result = new List<DependencyStatus>();
        foreach (var definition in Definitions)
        {
            string? found = definition.Candidates.Select(c => Path.Combine(gameRoot, c)).FirstOrDefault(File.Exists);
            found ??= paths.FirstOrDefault(p => definition.SearchTokens.Any(t =>
                Path.GetFileName(p).Replace(" ", "", StringComparison.Ordinal).Contains(t.Replace(" ", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase)));

            var details = "не найден";
            if (found is not null)
            {
                var version = TryGetVersion(found);
                details = version is null ? "обнаружен" : $"версия {version}";
            }
            result.Add(new DependencyStatus(definition.Name, definition.Minimum, found is not null, details));
        }
        return result;
    }

    private static string? TryGetVersion(string path)
    {
        if (Directory.Exists(path)) return null;
        try
        {
            var version = FileVersionInfo.GetVersionInfo(path).ProductVersion;
            return string.IsNullOrWhiteSpace(version) ? null : version.Split('+')[0];
        }
        catch { return null; }
    }
}

