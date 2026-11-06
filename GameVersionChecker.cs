using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CyberpunkVRInstaller;

internal static partial class GameVersionChecker
{
    public const string SupportedGameVersion = "2.5";
    public const string SupportedExecutableVersion = "3.0.88.46235";

    public static GameVersionResult Check(string gameRoot)
    {
        var executable = Path.Combine(gameRoot, "bin", "x64", "Cyberpunk2077.exe");
        if (!File.Exists(executable))
            return new(GameVersionState.Missing, null, L.T("Cyberpunk2077.exe не найден", "Cyberpunk2077.exe not found"));
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executable);
            var candidates = new[] { info.ProductVersion, info.FileVersion }
                .Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray();
            var detected = candidates.Select(ExtractVersion).FirstOrDefault(value => value is not null);
            var marketingCompatible = candidates.Any(value => Regex.IsMatch(value, @"(^|\D)2[\.,]5(\D|$)"));
            if (detected is null)
                return marketingCompatible
                    ? new(GameVersionState.Compatible, SupportedGameVersion, $"Cyberpunk 2077 {SupportedGameVersion}")
                    : new(GameVersionState.Unknown, null, L.T("версия EXE не указана", "EXE version is not specified"));

            var compatible = string.Equals(detected, SupportedExecutableVersion, StringComparison.OrdinalIgnoreCase) ||
                marketingCompatible;
            return compatible
                ? new(GameVersionState.Compatible, detected, $"Cyberpunk 2077 {SupportedGameVersion}, build {detected}")
                : new(GameVersionState.Incompatible, detected, L.T(
                    $"обнаружен build {detected}; требуется {SupportedExecutableVersion} (игра {SupportedGameVersion})",
                    $"detected build {detected}; {SupportedExecutableVersion} is required (game {SupportedGameVersion})"));
        }
        catch (Exception ex)
        {
            return new(GameVersionState.Unknown, null, L.T($"не удалось прочитать версию: {ex.Message}",
                $"could not read version: {ex.Message}"));
        }
    }

    public static void EnsureCompatible(string gameRoot)
    {
        var result = Check(gameRoot);
        if (result.State != GameVersionState.Compatible)
            throw new InstallerException(L.T("Несовместимая версия Cyberpunk 2077.\n\n",
                "Incompatible Cyberpunk 2077 version.\n\n") + result.Details + L.T(
                $"\n\nЭтот VR-порт рассчитан на Cyberpunk 2077 {SupportedGameVersion}.",
                $"\n\nThis VR port is designed for Cyberpunk 2077 {SupportedGameVersion}."));
    }

    private static string? ExtractVersion(string value)
    {
        var match = VersionRegex().Match(value.Replace(',', '.'));
        return match.Success ? match.Value : null;
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+\.\d+")]
    private static partial Regex VersionRegex();
}

internal enum GameVersionState
{
    Missing,
    Unknown,
    Compatible,
    Incompatible
}

internal sealed record GameVersionResult(GameVersionState State, string? DetectedVersion, string Details);
