# Troubleshooting

**English** | [Русский](TROUBLESHOOTING_RU.md)

## Game not detected

Select **Browse…** and choose the Cyberpunk 2077 root directory. It must contain
`bin\x64\Cyberpunk2077.exe`.

## Incompatible game version

Cyberpunk VR Port 0.2.3 targets Cyberpunk 2077 2.5. Do not bypass the version check: the native plugin
uses addresses from a specific game build.

## Crash after installation

1. Close the game.
2. Run the installer as administrator.
3. Enable **Safe Mode** and wait for the green verification message.
4. Start a new game instead of loading a modded save.

If a new game works, restore mods in groups until the conflict is identified. Saves marked `isModded`
may fail to load while their required mods are disabled.

## Watchdog timeout

`Watchdog timeout! (120 seconds)` means that the main game loop did not respond for two minutes. It is
not necessarily a DLL crash. If the report contains `IsLoadingSavedSession=true`, test a new game and
an earlier save.

## Old dependency mods

VR Port 0.2.3 does not require More Occluders or the former CET/ArchiveXL/TweakXL/Codeware stack.
More Occluders Scripted v0.3 has known compilation problems with Cyberpunk 2077 2.5; remove or update it
if redscript compilation fails. Shared frameworks may stay installed when required by other mods.

## Files to attach to an issue

- a screenshot of the installer;
- `report.txt` and `stacktrace.txt` from `%LOCALAPPDATA%\REDEngine\ReportQueue`;
- `bin\x64\cyberpunkvrport.log`, if present;
- exact game, installer, and component versions;
- whether the problem occurs in Safe Mode and in a new game.

Do not publish `Cyberpunk2077.dmp` or save files unless necessary. They are large and may contain
sensitive data.

