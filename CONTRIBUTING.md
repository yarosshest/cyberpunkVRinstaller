# Contributing

**English** | [Русский](CONTRIBUTING_RU.md)

Thank you for helping improve the project.

## Before you begin

1. Open an issue describing the bug or proposed change.
2. Do not commit the game, save files, crash dumps, or Nexus Mods archives.
3. Do not update the embedded VR Port without prior discussion: version 0.2.3 is intentionally pinned.

## Local validation

```powershell
dotnet restore --runtime win-x64
dotnet build --configuration Release --runtime win-x64
dotnet ./bin/Release/net8.0-windows/win-x64/CyberpunkVRPortInstaller.dll --self-test
pwsh ./build.ps1
```

## Change requirements

- preserve Windows 10/11 x64 compatibility;
- make file operations reversible and verify them after completion;
- never remove user files without a manifest or backup;
- use HTTPS and an official source for new downloads;
- never log tokens, personal paths, or save-file contents;
- add a self-test for new installation and restoration logic.

In a pull request, describe the problem, solution, validation steps, and user-facing risks.

