using System.Globalization;

namespace CyberpunkVRInstaller;

internal enum AppLanguage
{
    Russian,
    English
}

internal static class L
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CyberpunkVRPortInstaller", "language.txt");

    public static AppLanguage Language { get; private set; } =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Russian
            : AppLanguage.English;

    public static bool IsRussian => Language == AppLanguage.Russian;

    public static string T(string russian, string english) => IsRussian ? russian : english;

    public static void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            Language = File.ReadAllText(SettingsPath).Trim().Equals("ru", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.Russian
                : AppLanguage.English;
        }
        catch { }
    }

    public static void Toggle()
    {
        Language = IsRussian ? AppLanguage.English : AppLanguage.Russian;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, IsRussian ? "ru" : "en");
        }
        catch { }
    }
}
