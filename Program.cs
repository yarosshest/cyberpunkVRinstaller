namespace CyberpunkVRInstaller;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            return SelfTest.Run();
        if (args.Contains("--online-self-test", StringComparer.OrdinalIgnoreCase))
            return SelfTest.RunOnlineAsync().GetAwaiter().GetResult();
        if (args.Length >= 2 && args[0].Equals("--update-game", StringComparison.OrdinalIgnoreCase))
            return UpdateGame(args[1]);

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    private static int UpdateGame(string gameRoot)
    {
        try
        {
            gameRoot = Path.GetFullPath(gameRoot);
            GameVersionChecker.EnsureCompatible(gameRoot);

            var safeMode = new ModSafeModeManager();
            var safeModeStatus = safeMode.GetStatus(gameRoot);
            if (safeModeStatus.IsActive)
            {
                Console.WriteLine("Восстановление модов из Safe Mode…");
                if (!safeModeStatus.Verified)
                    safeMode.Repair(gameRoot);
                safeMode.Deactivate(gameRoot);
            }

            var missing = DependencyChecker.Check(gameRoot).Where(component => !component.Found).ToArray();
            if (missing.Length > 0)
                throw new InstallerException("Не найден обязательный компонент: " +
                    string.Join(", ", missing.Select(component => component.Name)));

            Console.WriteLine($"Установка Cyberpunk VR Port {InstallerEngine.ModVersion}…");
            var engine = new InstallerEngine();
            engine.Install(gameRoot);
            var state = engine.GetModState(gameRoot);
            if (state.State != VrModState.Enabled || state.Problems.Count > 0)
                throw new InstallerException("Проверка новой установки не пройдена:\n• " +
                    string.Join("\n• ", state.Problems));

            Console.WriteLine($"Cyberpunk VR Port {InstallerEngine.ModVersion} установлен и проверен.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ОБНОВЛЕНИЕ НЕ ВЫПОЛНЕНО: " + ex.Message);
            return 1;
        }
    }
}

