# Changelog

All notable changes to DeskNest are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.8.7.0] - 2026-11-19

### Added

- Per-nest display sorting from a new button in the nest title bar: Manual (default), Name, Type, Size, Date created and Date modified, each ascending or descending. The setting is independent for every nest, survives restarts, and is deliberately excluded from display layout profiles. Folders always come first; names use a natural, case- and accent-insensitive comparison; unreadable or missing items are pushed to the end. Manual item order is never rewritten by a sort, and drag reordering is disabled while a sort is active. Metadata is read once per item outside the UI thread through a bounded, path-keyed cache invalidated on last write. Folder Portals re-sort at every folder level and after watched content changes.
- **New to-do list** and **New note** entries in the nest right-click menu and in the title-bar menu button, using the same quick-actions creation path as the palette and the global search row (cursor placement, defaults, inline title edit).
- **New to-do list** and **New note** entries in the tray menu and in the desktop background context menu. They use the existing command-line / single-instance pipe mechanism (`--new-todo`, `--new-note`), with the verb list centralized in `DeskNestCommands`. Installer-written registry entries make the desktop menu persistent across application exits; an update to an existing installation receives all four verbs without a migration step.

### Changed

- Nest title bar layout: the menu button remains rightmost with the sort button before it; the portal Back chevron now sits at the far left before the nest icon and title (portal subfolders only), and every nest has an 8 DIP inset before the leading element. The title takes the remaining space and truncates with an ellipsis; the hamburger glyph is replaced by three vertical dots drawn as a `Path`/`EllipseGeometry` in the title foreground with the same hit area and hover behavior. Rolled-up and hidden-title behavior is unchanged.
- The desktop background context menu is no longer deleted when DeskNest exits; labels and the executable path are refreshed at startup and on language change, and only the uninstaller removes the entries.

### Fixed

- The title-bar sort button now opens its menu exactly like the nest menu button, and Notes and To-do nests get their own folded-page and checklist icons (shared with the quick-action tiles) instead of the generic grid icon.
- Natural name comparison now strips diacritics before calling `StrCmpLogicalW`, so accented and unaccented spellings sort together while keeping natural number ordering and case insensitivity.

## [0.8.6.0] - 2026-11-18

### Added

- Three nest types: classic file nests (now the default, including Folder Portals), plain-text Notes nests, and To-do list nests. Existing layouts load unchanged and are copied once to `layout.json.v3.bak` before the first write by this version.
- Notes nests store UTF-8 text in `%APPDATA%\DeskNest\Notes\<nestId>.txt` with deferred (default 800 ms) atomic saves, native undo, word wrap and a configurable font size. Failed writes keep the text in memory and are retried.
- To-do nests with inline editing, deletion, drag reorder with a drop indicator, strike-through for checked tasks, optional checked-to-bottom sorting, top/bottom insertion and a `done/total` count.
- A "Notes & Todo List" Settings tab with all new options, including the `Show quick actions in search` toggle.
- A quick-actions palette on tray double-click and a shared compact quick-actions row in the global search palette, with five mouse/keyboard-driven actions. New nests are placed at the cursor, clamped to the work area and offset to avoid covering visible nests.
- Localized default titles for new nests: `Todo N` and `Note N`, using the lowest unused number among nests of the same type (existing titles are never renamed).
- A visible `+` validate button next to the task input, plus per-task edit and delete buttons and inline validate/cancel controls when editing a task.
- A global **Todo font size** setting in the "Notes & Todo List" tab (default 12 pt), applied immediately to open lists; layouts saved without the key load with the default.
- Crash-safe Notes drafts: while editing, text is written atomically to `<id>.draft.txt`, recovered into edit mode at startup, promoted on Validate and removed on Cancel; deleting a nest removes its note and draft.

### Changed

- The zone search bar now filters To-do tasks live (case/accent/kana-insensitive, all words, matched/total footer, "No matching task" message, drag disabled while filtering, filter cleared on add and roll-up); Notes nests no longer create a search box, so the reclaimed space goes to the note text.
- Tray double-click now opens the quick-actions palette instead of Settings; the tray menu and its commands are unchanged.
- Quick-action buttons never take focus or appear in the Tab order, so typing and arrow keys keep controlling search results.
- Notes are read-only by default (no caret, normal cursor) with an explicit Edit button. `Enter` inserts a newline and `Ctrl+Enter` validates; `Esc` cancels, confirming only when the text changed. The deferred save writes a draft while editing instead of the note itself, which is only written on Validate.

### Fixed

- Adding a task with `Enter` did nothing: `NestWindow.Window_PreviewKeyDown` is a tunneling handler that treated every `Enter` as `OpenSelected()` and set `e.Handled = true` before the task input's `KeyDown` could run. It now returns early when keyboard focus is inside a Notes/To-do nest, so their own handlers receive the keys. The same guard stops `Delete` and `Ctrl` shortcuts from being consumed while typing, and focused alt content counts as "busy" so hover collapse cannot destroy in-progress text.

## [0.8.5.0] - 2026-11-18

### Added

- Display layout profiles keyed by monitor bounds, DPI scale and primary flag. Version 2 layouts are migrated once, with the original file copied to `layout.json.v2.bak`.
- Display-change handling with a 1.5 s debounce: existing profiles are restored exactly when monitors return, and unknown configurations are adapted from the closest known profile.
- Work-area anchoring and clamping for nests, including the expanded size of rolled-up zones and nests moved from a disconnected monitor to the primary screen.

### Fixed

- Nests are no longer left off-screen after a monitor change, and right-aligned nests keep their right anchor.

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

[0.8.7.0]: https://github.com/aziizfarhat/DeskNest/releases/tag/v0.8.7.0
[0.8.6.0]: https://github.com/aziizfarhat/DeskNest/releases/tag/v0.8.6.0
[0.8.5.0]: https://github.com/aziizfarhat/DeskNest/releases/tag/v0.8.5.0
[0.8.4.0]: https://github.com/aziizfarhat/DeskNest/releases/tag/v0.8.4.0
[0.8.3.4]: https://github.com/aziizfarhat/DeskNest/releases/tag/v0.8.3.4
[0.8.3.3]: https://github.com/aziizfarhat/DeskNest/releases/tag/v0.8.3.3
[0.8.3.2]: https://github.com/aziizfarhat/DeskNest/releases/tag/v0.8.3.2
[0.8.3.1]: https://github.com/aziizfarhat/DeskNest/releases/tag/v0.8.3.1
[0.8.3.0]: https://github.com/aziizfarhat/DeskNest/releases/tag/v0.8.3.0
