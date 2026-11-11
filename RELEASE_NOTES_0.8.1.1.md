# DeskNest 0.8.1.1

DeskNest 0.8.1.1 combines the 0.8.1.0 and 0.8.1.1 changes: reliable item undo across the desktop and zones, global search fixes, and applying zone templates to existing zones.

## What's new in 0.8.1.0

### Item undo across the desktop and zones

Moving an item between the desktop and a zone is now fully undoable.

- Drag an item from the desktop into a zone, remove it with `Del`, then press `Ctrl+Z`: the item returns to the zone and is visible again.
- Drag an item from a zone back to the desktop, then press `Ctrl+Z`: the item returns to the zone instead of undoing an earlier action.
- Undo commands resolve the item's current location, so a file moved between the managed storage and the desktop is re-adopted correctly.
- Removing an item from a zone and moving it to the desktop are recorded as separate history steps.
- Permanent deletion to the Recycle Bin remains outside the undo history.

### Global search improvements

- `Escape` now closes the search palette reliably, including when an input method is active.
- Fixed the error shown on the first double-click after startup: “Cannot set Visibility or call Show, ShowDialog, Close, or WindowInteropHelper.EnsureHandle while a Window is closing.”
- Opening a result now waits until the palette has finished closing before activating the zone or launching the item.

### Applying zone templates

- A new “Apply a template” submenu in the zone context menu applies a saved template to the current zone without touching its items.
- A new **Templates** tab in Settings lists every saved template and lets you:
  - apply a template to any existing zone;
  - create a new zone from a template;
  - save a zone as a template;
  - delete a template.
- The tray menu now has a direct “Templates…” entry.
- Applying a template is recorded in the undo history.

## Fixed in 0.8.1.1

### Global search

- Double-clicking a search result no longer hides all zones. The desktop double-click handler now ignores clicks that land on a DeskNest window, even when the palette closes before the deferred handling runs.

## Shortcuts

| Action | Shortcut |
|---|---|
| Show or hide all zones | `Ctrl+Alt+F` |
| Global search | `Ctrl+Shift+Space` |
| Close the search palette | `Escape` |
| Undo | `Ctrl+Z` |
| Redo | `Ctrl+Y` or `Ctrl+Shift+Z` |

## Upgrade notes

- Existing zones, settings, templates, item references, and shortcuts are preserved.
- The installer filename remains `DeskNest-Setup.exe`.
- Windows 10 or Windows 11, 64-bit, is required.

## Known limitations

- The installer interface is currently available in French only.
- Automatic updates require the release to include a signed `DeskNest-Setup*.exe` asset.
