# DeskNest 0.8.0

DeskNest 0.8.0 is a major workflow update. It adds two new interface languages, layout undo/redo, a global search palette, snapping with alignment guides, reusable zone templates, and automatic updates backed by Authenticode code signing.

## Highlights

### Russian and Ukrainian interfaces

The interface is now available in twelve languages, including Russian and Ukrainian. Every screen, menu, dialog, and notification is translated, with English used as a fallback for any missing entry.

### Undo and redo

Layout changes can now be undone and redone with `Ctrl+Z` and `Ctrl+Y` (`Ctrl+Shift+Z` also redoes).

- Moving, resizing, renaming, and restyling zones.
- Creating, duplicating, and deleting zones.
- Adding or removing item references.
- Applying a template, a zone preset, or a batch of appearance settings.
- Distributing zones across the screen.

Rapid consecutive changes to the same zone are grouped into a single history step. Undo and redo are also available from the zone and tray context menus.

### Global search

Press `Ctrl+Shift+Space` to open the search palette. It searches every zone, every folder portal, and the desktop in one place.

- Type to filter results instantly.
- Select a result to reveal it in its zone, or open it directly.
- Results show their source zone or portal and the item path.

The shortcut is registered globally; the tray menu also exposes the search command.

### Snapping and alignment guides

Zones now snap to each other and to the screen edges while being moved or resized.

- Alignment guides appear when edges or centers line up.
- Snapping works in both directions and respects a configurable threshold.
- The new “Distribute zones” command arranges all zones evenly across the screen.
- Snapping can be disabled in Settings or from the tray menu.

### Zone templates and duplication

- Duplicate a complete zone, including its items, appearance, and view mode.
- Save a zone as a template and recreate it later with the same color, size, fonts, and display mode.
- Templates are managed from the zone and tray menus and stored with the rest of the DeskNest data.

### Automatic updates and code signing

- DeskNest checks the latest GitHub release shortly after startup and from the tray menu.
- When a newer version is available, the installer is downloaded, its size and Authenticode signature are verified, and it can be installed silently.
- The “Check for updates now” button in Settings and the tray entry show the current status.
- Release builds can be signed with `signtool` through the `DESKNEST_PFX` and `DESKNEST_PFX_PASSWORD` environment variables. Signed releases help Windows SmartScreen establish trust in the installer.

Unsigned installers are rejected by the automatic updater. Automatic updates therefore require a signed release.

## Shortcuts

| Action | Shortcut |
|---|---|
| Show or hide all zones | `Ctrl+Alt+F` |
| Global search | `Ctrl+Shift+Space` |
| Undo | `Ctrl+Z` |
| Redo | `Ctrl+Y` or `Ctrl+Shift+Z` |

## Upgrade notes

- Existing zones, settings, templates, and shortcuts are preserved.
- The installer filename remains `DeskNest-Setup.exe`.
- Automatic updates only install Authenticode-signed installers.
- Windows 10 or Windows 11, 64-bit, is required.

## Known limitations

- The installer interface is currently available in French only.
- Automatic updates require the release to include a signed `DeskNest-Setup*.exe` asset.
