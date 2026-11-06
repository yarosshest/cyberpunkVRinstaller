# Changelog

**English** | [Русский](CHANGELOG_RU.md)

All notable project changes are documented in this file.

## [0.2.2] - 2026-11-07

### Changed

- updated the embedded Cyberpunk VR Port payload from 0.1.9 to 0.2.2;
- reduced required components to RED4ext 1.31.0 or newer;
- added migration and backup handling for the old 0.1.9 layout;
- added verified payload download for source and CI builds;
- retained the requested release filename `CyberpunkVRPortInstaller-0.1.9.exe`.

## [0.1.9] - 2026-11-06

### Added

- self-contained Cyberpunk VR Port 0.1.9 installation;
- Steam/GOG/Epic detection and Cyberpunk 2077 2.5 validation;
- GitHub and Nexus component installation, including RAR/7z support;
- backup, removal, and file restoration workflows;
- a separate VR-port disable mode;
- verified Safe Mode for RED4ext, CET, REDmod, ASI/OpenVR, script, and archive mods;
- repair support for manifests created by older Safe Mode versions;
- built-in self-tests for installation, disabling, and restoration.

