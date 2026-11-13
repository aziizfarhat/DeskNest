# DeskNest 0.8.2.0

DeskNest 0.8.2.0 focuses on the look of zones and on discoverability: per-zone backgrounds (solid, gradient, image, acrylic), real thumbnails for images, videos and PDF files, smoother animations that respect the Windows setting, a keyboard shortcut reference in Settings, and an installer available in all twelve DeskNest languages.

## What's new

### Per-zone backgrounds

Each zone can now use its own background, independently of the others.

- **Solid colour** keeps the previous behaviour.
- **Gradient** blends two colours with an adjustable angle (0–359°).
- **Image** uses a picture chosen on your computer. The zone tint keeps labels readable over bright photos.
- **Acrylic** blurs the current wallpaper behind the zone. The crop follows the zone's position and size, works with stretched, fitted, filled, centred and spanned wallpapers, and falls back to the solid colour when the wallpaper cannot be read.

Backgrounds are configured in **Settings › Zones › Zone background** and are stored per zone, so they survive restarts, undo/redo, duplication and templates.

### Thumbnails for images, videos and PDF

- Images, videos and PDF files now display a real thumbnail instead of a generic icon.
- Thumbnails come from the same provider as File Explorer (`IShellItemImageFactory`), so installed codecs and PDF preview handlers are used automatically.
- Thumbnails are cached separately from icons (bounded LRU cache) and extracted in the background.
- Unsupported formats fall back to the usual shell icon.
- Thumbnails can be turned off in **Settings › Behavior**; zones then use regular file icons.

### Open items with a single click

- A new option in **Settings › Behavior** opens items with a single click instead of a double-click.
- It is off by default, so the previous double-click behaviour is unchanged unless you enable it.

### Tray icon

- Double-clicking the notification-area icon now opens **Settings** (previously it hid or showed the zones). Hiding and showing zones remains available from the tray menu and `Ctrl+Alt+F`.

### Smoother animations

- Roll-up and hover animations use a gentler quartic ease-out curve.
- When Windows is set to reduce animations (Settings › Accessibility › Visual effects › Animation effects off), DeskNest now disables its own animations instead of playing them.
- The icon hover effect is animated in code so it can follow that setting too.

### Keyboard shortcuts reference

- A new **Shortcuts** tab in Settings lists every keyboard and mouse shortcut, grouped into General, Zones and Items.
- The tab is translated into all twelve languages.

### Multilingual installer

- The installer is now available in English, French, Italian, German, Spanish, Portuguese, Arabic, Simplified Chinese, Japanese, Korean, Russian and Ukrainian.
- The task description, the post-install launch entry and the custom uninstall dialog are translated for each language.
- Simplified Chinese uses a vendored Inno Setup translation (`installer-lang/ChineseSimplified.isl`).

## Fixed

- **Zones no longer disappear after an update.** If `layout.json` is missing, unreadable or contains no zone, DeskNest restores the most recent non-empty backup (or the `layout-last-good.json` copy). An empty backup can never overwrite a non-empty one, and an intentional deletion of the last zone is remembered so it is not "restored" on the next start.
- **Importing a layout no longer duplicates zones.** Zones are de-duplicated by identifier when loading, and only one window can exist per zone.
- **Roll up and roll down are smooth again.** The window keeps its full height during the animation and only its clip rectangle is animated, so the content is revealed progressively instead of jumping.
- **Fixed a crash when hovering an icon** (`Cannot animate the 'ScaleX' property ... because the object is sealed or frozen`).
- **A zone expanded by hover now collapses reliably**, even when Windows does not send a mouse-leave event: the cursor position is checked periodically while the zone is expanded.
- **The Settings window no longer freezes while saving.** Serialization stays on the UI thread, but the disk write and the daily backup run on a coalesced background worker, so dragging a slider no longer waits for the disk. Operations that still take more than 200 ms are logged to `diagnostic.log`.
- Icon extraction and thumbnail extraction now share a single shell worker thread, avoiding first-run COM races.

## Shortcuts

| Action | Shortcut |
|---|---|
| Show or hide all zones | `Ctrl+Alt+F` |
| Global search | `Ctrl+Shift+Space` |
| Roll up or unroll a zone | Double-click its title |
| Close a dialog or clear the selection | `Escape` |
| Undo | `Ctrl+Z` |
| Redo | `Ctrl+Y` or `Ctrl+Shift+Z` |
| Open the selected item | `Enter` |
| Rename the selected item | `F2` |
| Remove from a zone | `Del` |
| Delete permanently (Recycle Bin) | `Shift+Del` |
| Previous folder in a Folder Portal | `Alt+←`, `Backspace` or the mouse back button |

## Upgrade notes

- Existing zones, settings, templates, item references and shortcuts are preserved.
- Zones created before this version use the solid colour background by default.
- The installer is named `DeskNest-Setup-<version>.exe`; the portable archive is named `DeskNest-<version>-Portable.zip`.
- Windows 10 or Windows 11, 64-bit, is required.

## Known limitations

- Automatic updates require the release to include a signed `DeskNest-Setup*.exe` asset.
- Acrylic is a wallpaper crop blurred by DeskNest, not the DWM backdrop material; it therefore looks the same on Windows 10 and Windows 11.
- PDF thumbnails require a PDF preview handler (installed by most PDF readers).
