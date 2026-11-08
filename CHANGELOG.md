# Changelog

**English** | [Русский](CHANGELOG_RU.md)

All notable project changes are documented in this file.

## [0.2.3] - 2026-11-08

### Changed

- updated the embedded Cyberpunk VR Port payload from 0.2.2 to 0.2.3;
- updated the verified payload checksum, installer metadata, release filename, UI marker, and documentation for 0.2.3;
- retained the Cyberpunk 2077 2.5 and RED4ext 1.31.0 compatibility requirements from upstream.

## [0.2.2] - 2026-11-07

### Changed

- added a persistent English/Russian UI switch and localized installer, Safe Mode, status, dialog, and error messages;
- updated the embedded Cyberpunk VR Port payload from 0.1.9 to 0.2.2;
- reduced required components to RED4ext 1.31.0 or newer;
- added migration and backup handling for the old 0.1.9 layout;
- added verified payload download for source and CI builds;
- aligned the installer version and release filename with VR Port 0.2.2: `CyberpunkVRPortInstaller-0.2.2.exe`.

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

