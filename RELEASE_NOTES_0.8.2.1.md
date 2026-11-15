# DeskNest 0.8.2.1

DeskNest 0.8.2.1 improves reliability and everyday comfort: the global search palette keeps keyboard focus, single-click opening no longer launches an item twice, undo restores items to their exact original position, and Settings uses a clearer left-hand navigation panel with the new DeskNest branding.

## What's new

### Global search keeps the keyboard

- The search palette receives keyboard focus as soon as it opens, even when another application was in the foreground.
- While the palette is open, keystrokes are captured instead of reaching the desktop or the application behind it.
- System shortcuts (`Alt+Tab`, the Windows key, `Alt+F4`, `Alt+Esc`, `Ctrl+Esc`) still work, so the palette never traps the user.
- `Escape` closes the palette; clicking outside it now closes it as well.

### Settings navigation

- Settings now uses a vertical navigation panel on the left, with one icon per section and the selected section displayed on the right.
- All existing settings, translations, right-to-left layouts and keyboard behaviour are preserved.
- The window is slightly larger to give each section more room.

### Branding and About

- The new DeskNest logo is used as the application icon (window, taskbar, tray and installer) at every Windows icon size, from 16×16 to 256×256.
- The About section displays the DeskNest logo with its name and a clickable link to the project on GitHub: <https://github.com/aziizfarhat/DeskNest>.

## Fixed

- **Single-click opening no longer launches an item twice.** A short debounce based on the Windows double-click time ignores a second activation of the same item.
- **Undo now restores the original location.** Moving an item from a zone to the desktop puts it back in its original zone and position; moving it from the desktop to a zone restores its exact desktop path and position.
- **Removing several items at once is a single undo step.** `Ctrl+Z` brings the whole selection back to the zone in one action.
- **Dropping several items on the desktop is a single undo step** as well.
- The desktop path reached by an item is remembered before its transfer record is forgotten, so redo can put it back at the same place.

## Shortcuts

| Action | Shortcut |
|---|---|
| Show or hide all zones | `Ctrl+Alt+F` |
| Global search | `Ctrl+Shift+Space` |
| Roll up or unroll a zone | Double-click its title |
| Close a dialog, the search palette, or clear the selection | `Escape` |
| Undo | `Ctrl+Z` |
| Redo | `Ctrl+Y` or `Ctrl+Shift+Z` |
| Open the selected item | `Enter` |
| Rename the selected item | `F2` |
| Remove from a zone | `Del` |
| Delete permanently (Recycle Bin) | `Shift+Del` |
| Previous folder in a Folder Portal | `Alt+←`, `Backspace` or the mouse back button |

## Upgrade notes

- Existing zones, settings, templates, item references and shortcuts are preserved.
- The installer is named `DeskNest-Setup-<version>.exe`; the portable archive is named `DeskNest-<version>-Portable.zip`.
- Windows 10 or Windows 11, 64-bit, is required.

## Known limitations

- Automatic updates require the release to include a signed `DeskNest-Setup*.exe` asset.
- Acrylic is a wallpaper crop blurred by DeskNest, not the DWM backdrop material; it therefore looks the same on Windows 10 and Windows 11.
- PDF thumbnails require a PDF preview handler (installed by most PDF readers).
