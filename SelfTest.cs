using System.IO.Compression;
using System.Diagnostics;
using System.Text.Json;

namespace CyberpunkVRInstaller;

internal static class SelfTest
{
    public static int Run()
    {
        var root = Path.Combine(Environment.CurrentDirectory, ".selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "bin", "x64"));
            File.WriteAllText(Path.Combine(root, "bin", "x64", "Cyberpunk2077.exe"), "test executable");
            Assert(GameVersionChecker.Check(root).State == GameVersionState.Unknown, "unversioned game executable was not rejected");
            try
            {
                GameVersionChecker.EnsureCompatible(root);
                throw new InvalidOperationException("version guard accepted an unversioned executable");
            }
            catch (InstallerException) { }

            var originalPath = Path.Combine(root, "bin", "x64", "openvr_api.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
            File.WriteAllText(originalPath, "original file");

            var obsolete = Path.Combine(root, "archive", "pc", "mod", "VRCigarette.archive.xl");
            Directory.CreateDirectory(Path.GetDirectoryName(obsolete)!);
            File.WriteAllText(obsolete, "obsolete original");

            var oldPlugin = Path.Combine(root, "red4ext", "plugins", "CyberpunkVR_Stereo", "CyberpunkVR_Stereo.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(oldPlugin)!);
            File.WriteAllText(oldPlugin, "VR Port 0.1.9 fixture");

            var engine = new InstallerEngine();
            engine.Install(root);
            Assert(engine.IsInstalled(root), "manifest was not created");
            Assert(File.Exists(Path.Combine(root, "red4ext", "plugins", "cyberpunkvrport", "cyberpunkvrport.dll")), "main plugin missing");
            Assert(!File.Exists(obsolete), "obsolete file was not removed");
            Assert(!File.Exists(oldPlugin), "VR Port 0.1.9 plugin was not removed during migration");

            var disabled = engine.Disable(root);
            Assert(disabled.State == VrModState.Disabled, "VR mod did not enter disabled state");
            Assert(!File.Exists(Path.Combine(root, "red4ext", "plugins", "cyberpunkvrport", "cyberpunkvrport.dll")),
                "VR plugin remains active after disable");
            Assert(File.ReadAllText(originalPath) == "original file", "original overwritten file was not restored while disabled");
            Assert(engine.GetModState(root).Problems.Count == 0, "disabled VR verification failed");
            var enabled = engine.Enable(root);
            Assert(enabled.State == VrModState.Enabled, "VR mod did not return to enabled state");

            var loader = Path.Combine(root, "bin", "x64", "winmm.dll");
            File.WriteAllText(loader, "RED4ext loader fixture");
            var standalonePlugin = Path.Combine(root, "bin", "x64", "plugins", "TestStandalone", "test.asi");
            Directory.CreateDirectory(Path.GetDirectoryName(standalonePlugin)!);
            File.WriteAllText(standalonePlugin, "standalone plugin fixture");
            var openVr = Path.Combine(root, "bin", "x64", "openvr_api.dll");
            Assert(File.Exists(openVr), "installed OpenVR runtime fixture is missing");
            var redmod = Path.Combine(root, "mods", "TestRedmod", "info.json");
            Directory.CreateDirectory(Path.GetDirectoryName(redmod)!);
            File.WriteAllText(redmod, "{}");
            var safeMode = new ModSafeModeManager();
            var safeStatus = safeMode.Activate(root);
            Assert(safeStatus.IsActive && safeStatus.Verified, "safe mode verification failed");
            Assert(!File.Exists(loader), "mod loader remains active in safe mode");
            Assert(!File.Exists(standalonePlugin), "standalone ASI plugin remains active in safe mode");
            Assert(!File.Exists(openVr), "OpenVR runtime remains active in safe mode");
            Assert(!File.Exists(redmod), "REDmod remains active in safe mode");
            Assert(!Directory.Exists(Path.Combine(root, "archive", "pc", "mod")), "archive mods remain active in safe mode");
            var restoredSafeMode = safeMode.Deactivate(root);
            Assert(!restoredSafeMode.IsActive && File.ReadAllText(loader) == "RED4ext loader fixture", "safe mode restore failed");
            Assert(File.ReadAllText(standalonePlugin) == "standalone plugin fixture", "standalone ASI plugin was not restored");
            Assert(File.Exists(openVr), "OpenVR runtime was not restored");
            Assert(File.Exists(redmod), "REDmod was not restored");

            // An older manifest must be repairable without first restoring every mod.
            safeStatus = safeMode.Activate(root);
            var safeManifest = Path.Combine(root, ".cyberpunk-mod-safe-mode", "safe-mode.json");
            var oldManifest = JsonSerializer.Deserialize<SafeModeManifest>(File.ReadAllText(safeManifest))!;
            oldManifest.Items.RemoveAll(item => item.RelativePath.Equals(@"bin\x64\plugins", StringComparison.OrdinalIgnoreCase));
            File.WriteAllText(safeManifest, JsonSerializer.Serialize(oldManifest, new JsonSerializerOptions { WriteIndented = true }));
            Directory.CreateDirectory(Path.Combine(root, "bin", "x64", "plugins", "LatePlugin"));
            File.WriteAllText(Path.Combine(root, "bin", "x64", "plugins", "LatePlugin", "late.asi"), "late plugin");
            Assert(!safeMode.GetStatus(root).Verified, "incomplete old Safe mode was not detected");
            Assert(safeMode.Repair(root).Verified, "old Safe mode could not be repaired");
            Assert(!Directory.Exists(Path.Combine(root, "bin", "x64", "plugins")), "repair left plugins active");
            safeMode.Deactivate(root);

            var result = engine.Uninstall(root);
            Assert(result.PreservedModifiedFiles.Count == 0, "unexpected modified files");
            Assert(File.ReadAllText(originalPath) == "original file", "overwritten file was not restored");
            Assert(File.ReadAllText(obsolete) == "obsolete original", "obsolete file was not restored");
            Assert(File.ReadAllText(oldPlugin) == "VR Port 0.1.9 fixture", "old plugin was not restored after uninstall");
            Assert(!engine.IsInstalled(root), "manifest remains after uninstall");

            var componentArchive = Path.Combine(root, "test-component.zip");
            using (var archive = ZipFile.Open(componentArchive, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("Package/red4ext/plugins/TestComponent/TestComponent.dll");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("component payload");
            }
            var components = new ComponentInstaller();
            components.InstallLocalArchives(root, [componentArchive]);
            Assert(File.ReadAllText(Path.Combine(root, "red4ext", "plugins", "TestComponent", "TestComponent.dll")) == "component payload",
                "component wrapper folder was not normalized");
            Assert(ComponentInstaller.NexusComponents.Length == 0, "VR Port 0.2.3 must not require Nexus components");

            var sevenZip = @"C:\Program Files\7-Zip\7z.exe";
            if (File.Exists(sevenZip))
            {
                var sourceRoot = Path.Combine(root, "fixture", "Wrapper", "r6", "scripts");
                Directory.CreateDirectory(sourceRoot);
                File.WriteAllText(Path.Combine(sourceRoot, "MoreOccluders.reds"), "scripted v0.3 fixture");
                var sevenZipArchive = Path.Combine(root, "More-Occluders-Test.7z");
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = sevenZip,
                    Arguments = $"a -t7z \"{sevenZipArchive}\" \"{Path.Combine(root, "fixture", "Wrapper")}\\*\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                process?.WaitForExit();
                Assert(process?.ExitCode == 0 && File.Exists(sevenZipArchive), "could not create 7z test fixture");
                components.InstallLocalArchives(root, [sevenZipArchive]);
                Assert(File.ReadAllText(Path.Combine(root, "r6", "scripts", "MoreOccluders.reds")) == "scripted v0.3 fixture",
                    "embedded 7-Zip extraction failed");
            }
            Console.WriteLine("SELF-TEST PASSED");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("SELF-TEST FAILED: " + ex);
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    public static async Task<int> RunOnlineAsync()
    {
        var root = Path.Combine(Environment.CurrentDirectory, ".selftest-online-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "bin", "x64"));
            File.WriteAllText(Path.Combine(root, "bin", "x64", "Cyberpunk2077.exe"), "test executable");
            var installer = new ComponentInstaller();
            var result = await installer.InstallGitHubComponentsAsync(root, new InlineProgress(p => Console.WriteLine($"{p.Percent,3}% {p.Message}")));
            Assert(result.InstalledComponents.Count == 1, "RED4ext was not installed");
            var statuses = DependencyChecker.Check(root);
            foreach (var expected in new[] { "RED4ext" })
                Assert(statuses.Any(s => s.Name == expected && s.Found), $"component was not detected after install: {expected}");
            Console.WriteLine("ONLINE SELF-TEST PASSED");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ONLINE SELF-TEST FAILED: " + ex);
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class InlineProgress(Action<InstallProgress> report) : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) => report(value);
    }
}

