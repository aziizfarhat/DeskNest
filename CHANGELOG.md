# Changelog

All notable changes to DeskNest are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.8.4.0] - 2026-11-17

### Changed

- Lower memory footprint: display-sized and shared icon/background decoding, bounded undo history, translation tables loaded on demand, and snap guide windows released after use.
- Runtime configuration now uses workstation, non-concurrent garbage collection with `System.GC.ConserveMemory=5` and Tiered PGO disabled. No visible behavior or layout change.

## [0.8.3.4] - 2026-11-17

### Changed

- DeskNest is now released under the GNU General Public License v3.0 (GPL-3.0). Previous releases were distributed under CC BY-NC 4.0.
- The installer, README and About tab now reference the GPL-3.0 license.

### Removed

- Automatic update download, silent installer launch, and SHA-256/Authenticode verification. DeskNest now only compares its version with the latest GitHub release and opens the release page on request.

## [0.8.3.3] - 2026-11-17

### Added

- `CHANGELOG.md` covering the 0.8.3.x series.
- GitHub Actions workflow that builds the application and runs the test suite on every push and pull request.
- Installer metadata (publisher, support, updates and version information) and a license page.

### Changed

- README refreshed with the new version, license, source layout, test instructions and update verification details.
- Installer translations verified for all twelve supported languages.

## [0.8.3.2] - 2026-11-16

### Added

- `IFileSystem` abstraction with a `PhysicalFileSystem` implementation.
- `DeskNest.Tests` project with an in-memory file system covering save/load, backups, snapshots and recovery paths.

### Changed

- `NestManager` split into focused partial files (history, items, settings, zones, desktop, files).
- Sources reorganized into `Views/`, `Services/`, `Models/` and `Native/` folders; the `DeskNest` namespace is unchanged.
- Localization audited for duplicate keys and tables; no duplicates requiring a merge were found.

## [0.8.3.1] - 2026-11-16

### Added

- SHA-256 verification for downloaded updates, read from the release body or a published `.sha256` asset.
- `build.bat` now writes the installer checksum next to the setup executable.
- Configurable toggle and search hotkeys in Settings › Shortcuts, with duplicate and availability checks.

### Changed

- Authenticode signature verification is optional instead of mandatory, so unsigned builds can still update.
- Versions are displayed and compared with all four parts (for example `0.8.3.1`).
- The mouse hook callback now only performs detection, keeping low-level input handling short.

### Fixed

- The Public Desktop permission prompt no longer blocks startup; it is asked once, after the zones are visible.
- Hotkey errors now name the shortcut and point to the settings instead of showing a fixed combination.

## [0.8.3.0] - 2026-11-16

### Added

- Localized **Restore all items to desktop** command in the tray menu.
- Startup scan that returns orphaned items from the managed store to the desktop.
- Uninstall verification for the restore step and empty managed-store cleanup.

### Fixed

- `layout.json` is now written atomically through `File.Replace`, with `layout.json.bak` kept as the previous valid state and used when the main file is corrupt or empty.
- Moving an item into or out of the managed store now saves the layout synchronously, so a crash cannot lose the original location.

[0.8.4.0]: https://github.com/aziizfarhat/DeskNest/releases/tag/v0.8.4.0
[0.8.3.4]: https://github.com/aziizfarhat/DeskNest/releases/tag/v0.8.3.4
[0.8.3.3]: https://github.com/aziizfarhat/DeskNest/releases/tag/v0.8.3.3
[0.8.3.2]: https://github.com/aziizfarhat/DeskNest/releases/tag/v0.8.3.2
[0.8.3.1]: https://github.com/aziizfarhat/DeskNest/releases/tag/v0.8.3.1
[0.8.3.0]: https://github.com/aziizfarhat/DeskNest/releases/tag/v0.8.3.0
