using System.Diagnostics;

namespace CyberpunkVRInstaller;

internal static class DependencyChecker
{
    private sealed record Definition(string Name, string Minimum, string[] Candidates, string[] SearchTokens);

    private static readonly Definition[] Definitions =
    [
        new("RED4ext", "1.31.0", [@"red4ext\RED4ext.dll"], ["red4ext.dll"])
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

            var details = L.T("не найден", "not found");
            if (found is not null)
            {
                var version = TryGetVersion(found);
                details = version is null ? L.T("обнаружен", "detected") : L.T($"версия {version}", $"version {version}");
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

