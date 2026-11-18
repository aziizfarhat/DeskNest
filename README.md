# DeskNest

[![Build](https://github.com/aziizfarhat/DeskNest/actions/workflows/ci.yml/badge.svg)](https://github.com/aziizfarhat/DeskNest/actions/workflows/ci.yml)

<img src="logo/logo_name.png" alt="DeskNest" width="240">

**Organize your Windows desktop with customizable zones and folder portals.**

DeskNest is a lightweight Windows desktop organizer that helps you group applications, shortcuts, files, and folders into customizable desktop areas called *Nests*.

Instead of leaving everything scattered across your desktop, DeskNest provides a more organized and accessible workspace.

> **Project status:** Under active development. DeskNest is currently at version 0.8.6.0.

## Features

### Desktop Zones (Nests)

Create independent desktop areas to organize your items by category, purpose, or workflow.

- Create and rename zones.
- Select all items within a zone.
- Customize background colors and opacity.
- Choose a per-zone background: solid colour, gradient, image, or acrylic (blurred wallpaper).
- Preview images, videos, and PDF files with thumbnails instead of generic icons.
- Open items with a single click or a double-click (option in Settings › Behavior).
- Adjust icon size and fonts.
- Hide zone titles.
- Roll up zones to save desktop space.
- Lock zones.
- Expand zones automatically when hovering over them.
- Search the items of a zone from an optional search bar: To-do nests filter their tasks live (case/accent-insensitive, all words), while Notes nests create no search box and give the space to the note text.
- Link zones to folders.
- Delete zones.
- Undo and redo layout changes with `Ctrl+Z` / `Ctrl+Y`.
- Snap zones to each other and to the screen edges, with alignment guides.
- Distribute zones evenly across the screen.
- Duplicate a zone or save it as a reusable template (color, size, fonts, and view mode).
- Apply a saved template to an existing zone from the zone menu or the Settings › Templates tab.
- Move items between the desktop and a zone and undo the move with `Ctrl+Z`; the item returns to its original zone or desktop position.

### Folder Portals

Folder Portals provide convenient access to folder contents directly from your desktop.

- Create Folder Portals.
- Link zones to existing folders.
- Keep frequently used files and folders organized and accessible.

### Notes Nests

Notes nests are plain-text notepads that live directly on your desktop. Their text is kept in `%APPDATA%\DeskNest\Notes\<nestId>.txt` as UTF-8, separate from `layout.json`, so layouts stay small and notes survive layout changes. New notes are titled `Note N` (localized prefix, lowest unused number) and open empty in edit mode.

- A note is **read-only by default**: no caret and a normal cursor. Click the pencil (Edit) button to edit; a Validate (check) and Cancel (X) button then appear.
- `Enter` inserts a new line; `Ctrl+Enter` validates and returns to read-only. `Esc` cancels, asking for confirmation only if the text changed. Cancel restores the last saved text.
- While editing, the real note file is never written. Instead the text is saved to `%APPDATA%\DeskNest\Notes\<id>.draft.txt` about 800 ms after the last keystroke (configurable in Settings › Notes & Todo List), atomically. Validate promotes the draft to the note and removes it; Cancel removes it.
- If the app exits or crashes while editing, the draft is kept. On next startup a note with a draft opens in edit mode with the draft text, so nothing is lost.
- Saves are atomic: a temporary file is written and then replaces the previous one, with a backup kept for recovery. A failed write keeps the text in memory and retries instead of losing it.
- Native TextBox undo and redo (`Ctrl+Z` / `Ctrl+Y`) while editing; layout undo/redo never affects note text.
- Word wrap and a configurable font size. Rich text is intentionally not supported. Text is capped at 100,000 characters.
- Deleting a note nest deletes both its text file and its draft. A missing file simply opens as an empty note. Typing in a note never triggers nest hover collapse or the global hotkeys.

### To-do Lists

To-do nests keep simple task lists on your desktop. Each nest shows a `done/total` count and is titled `Todo N` (localized prefix, lowest unused number).

- Type a task in the input row and press `Enter` (main keyboard or numpad) or click the `+` button to add it. The input is cleared and keeps focus; empty or whitespace-only input adds nothing.
- Each row shows, in order: a drag grip, a checkbox, the task text, an edit (pencil) button, and a delete button. The checkbox is always visible and toggles strike-through / checked-to-bottom behavior.
- Click the pencil to edit a task in place: the text is selected, with a Validate (check) and Cancel (X) button. `Enter` or the check saves, `Esc` or X cancels and restores the old text. An empty save is refused and keeps the row in edit mode. A row being edited cannot be dragged.
- `Delete` removes the selected task. Checked tasks can be struck through and can optionally move to the bottom; new tasks can be added at the top or the bottom.
- Reorder tasks by dragging the grip at the left of a row: a drop indicator shows the target position, and file drag-and-drop is not affected.
- The font size is set globally with **Todo font size** in Settings › Notes & Todo List (default 12 pt) and applies immediately to open lists.
- Tasks are stored in `layout.json` with their id, text, and done state.

### Quick Actions

Double-click the DeskNest tray icon to open a compact quick-actions palette, styled like the global search palette. It offers five large buttons: **New zone**, **New Folder Portal**, **New to-do list**, **New note**, and **Settings**.

- Use the arrow keys, press `1`–`5`, or press `Enter` to run the highlighted action; `Esc` or a click outside closes the window.
- New nests are created centered at the mouse cursor, clamped to the screen's work area and offset so they do not cover visible nests. Their title is immediately ready to edit.
- Cancelling the folder picker creates nothing. The tray menu and its commands are unchanged.
- The same five actions appear as a slim icon row in the global search palette. The buttons never take keyboard focus and are not in the Tab order, so typing and arrow keys still control search results; clicking one runs the action and closes the palette.
- The search-palette row can be turned off with **Show quick actions in search** in Settings › Notes & Todo List.

### Global Search

Press `Ctrl+Shift+Space` to open the search palette and search every zone, folder portal, and the desktop at once. Selecting a result reveals the item in its zone or opens it directly. The palette takes keyboard focus as soon as it opens, so typing never reaches the desktop or another application. Press `Escape` or click outside the palette to close it.

### Windows Integration

DeskNest integrates with the Windows desktop environment.

- Dedicated context menu for desktop zones.
- Desktop background context menu for creating new zones and Folder Portals.
- Windows startup option.
- Configurable global hotkeys for toggling zones and opening the search palette (Settings › Shortcuts).
- Single-instance application support.
- Stays visible when using the Windows “Show desktop” command.
- Native Windows desktop integration.
- Optional startup check that compares the installed version with the latest GitHub release and opens its page when a newer version is available.
- Restore all items to the desktop from the tray menu.
- Recovery of items left in the managed store after a crash.
- Optional Authenticode code-signing for release builds.

The desktop background context menu is available while DeskNest is running and is removed when the application exits normally.
The desktop background context menu uses DeskNest's localization system.

### Multilingual Interface

DeskNest includes translations for twelve languages:

- English
- French
- Italian
- German
- Spanish
- Portuguese
- Arabic
- Simplified Chinese
- Japanese
- Korean
- Russian
- Ukrainian

## Screenshots

Screenshots will be added as the project documentation evolves.

## System Requirements

- Windows 10 or Windows 11
- 64-bit Windows

The standalone application is published as a self-contained executable, so the .NET Desktop Runtime is not required to run that build.

## Installation

DeskNest is distributed through an Inno Setup installer.

The installer provides:

- Installation without requiring administrator privileges.
- Optional automatic startup with Windows.
- A standard Windows uninstall entry.
- A shortcut in the Start Menu.
- The GPL-3.0 license text.
- An uninstall prompt that keeps zones, settings and backups by default, with an option to delete them.

The installer interface is available in the same twelve languages as DeskNest. Simplified Chinese uses the translation vendored in `installer-lang/` because Inno Setup does not ship it.

### Building the Installer

To build the installer:

1. Build and publish the application.
2. Make sure `publish\DeskNest.exe` exists.
3. Open `installer.iss` with Inno Setup.
4. Compile the installer.

The resulting installer is generated in the `installer` directory.

## Building from Source

### Requirements

- Windows 10 or Windows 11 (64-bit)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Git

### Clone the Repository

```powershell
git clone https://github.com/aziizfarhat/DeskNest.git
cd DeskNest
```

### Build and Run

```powershell
dotnet restore
dotnet build
dotnet run
```

### Run the Tests

```powershell
dotnet run --project DeskNest.Tests\DeskNest.Tests.csproj -c Release
```

The test project uses an in-memory file system, so it never reads or writes `%APPDATA%\DeskNest`.

### Create a Standalone Release

DeskNest includes a build script that automates the publishing and installer creation process.

Simply run:

```powershell
.\build.bat
```

The script automatically:

1. Reads the application version from `DeskNest.csproj`.
2. Publishes a self-contained, single-file Windows x64 executable.
3. Applies ReadyToRun compilation and removes debug symbols.
4. Signs the application with `signtool` when a certificate is configured.
5. Creates the portable ZIP archive.
6. Detects Inno Setup 6, if installed, and creates the Windows installer.
7. Signs the installer when a certificate is configured.

**Generated files:**

| Output | Description |
|---|---|
| `publish/DeskNest.exe` | Portable standalone executable |
| `publish/DeskNest-<version>-Portable.zip` | Portable archive containing `DeskNest.exe` and `LICENSE` |
| `installer/DeskNest-Setup-<version>.exe` | Windows installer (when Inno Setup 6 is available) |

For example, with version `0.8.6.0` the script produces `publish/DeskNest-0.8.6.0-Portable.zip` and `installer/DeskNest-Setup-0.8.6.0.exe`.

The target computer does not need the .NET Desktop Runtime to run the standalone release.

### Code Signing

Release builds can be signed with an Authenticode certificate. Set the following environment variables before running `build.bat`:

| Variable | Description |
|---|---|
| `DESKNEST_PFX` | Path to the `.pfx` code-signing certificate |
| `DESKNEST_PFX_PASSWORD` | Password of the certificate (optional) |

When `DESKNEST_PFX` is not set, the build succeeds but remains unsigned. Code signing applies to the release binaries only; DeskNest never downloads or runs an installer itself.

### Update Check

DeskNest only compares its installed version with the latest GitHub release. It never downloads or runs an installer. If a newer version exists, the dialog offers a button that opens the release page in the default browser. No data is sent besides the GitHub API request.

The startup check can be turned off in Settings › General. A manual check is available in Settings › About; it reports when the installed version is already the latest.

## Technology

DeskNest is built using:

- C#
- .NET 8
- Windows Presentation Foundation (WPF)
- Windows Forms
- Windows Registry integration
- Windows APIs
- Named Pipes for inter-process communication

Neither the application nor the test project declares external NuGet package dependencies.

## Project Structure

Some of the main source files:

| File | Purpose |
|---|---|
| `App.xaml.cs` | Application startup and lifecycle |
| `Views/NestWindow.xaml` | Zone interface (files, notes, to-do lists) |
| `Views/SettingsWindow.xaml` | Application settings, including Notes & Todo List |
| `Views/SearchPalette.xaml` | Global search palette with quick-actions row |
| `Views/QuickActionsWindow.xaml` | Tray quick-actions palette |
| `Views/QuickActionsBar.cs` | Shared quick-actions buttons for both palettes |
| `Views/GuideOverlay.cs` | Alignment guide overlay |
| `Views/TrayIcon.cs` | System tray integration |
| `Views/DesktopContextMenu.cs` | Desktop background context menu |
| `Services/NestManager*.cs` | Zone management, split by concern |
| `Services/Storage.cs` | Layout persistence, V3 backup and backups |
| `Services/NotesStore.cs` | Deferred, atomic notes persistence |
| `Services/TodoOrder.cs` | To-do ordering and insert/move rules |
| `Services/CursorPlacement.cs` | Cursor-screen placement and non-overlap logic |
| `Services/IFileSystem.cs` | File-system abstraction used by the tests |
| `Services/UpdateChecker.cs` | GitHub release version comparison |
| `Services/DesktopHelper.cs` | Windows desktop integration |
| `Services/DesktopStore.cs` | Desktop item management |
| `Services/DesktopWatcher.cs` | Desktop monitoring |
| `Services/CommandServer.cs` | Inter-process command handling |
| `Services/SnapEngine.cs` | Snapping and alignment calculations |
| `Services/Loc.cs` | Localization |
| `Models/Models.cs` | Data models |
| `Models/Undo.cs` | Undo and redo history |
| `Native/GlobalHotkey.cs` | Global hotkey registration |
| `DeskNest.Tests/` | Automated tests |
| `installer.iss` | Windows installer configuration |
| `CHANGELOG.md` | Release history |

## Contributing

Contributions, suggestions, and bug reports are welcome.

Before submitting changes:

1. Build the project using `dotnet build`.
2. Run the tests using `dotnet run --project DeskNest.Tests\DeskNest.Tests.csproj -c Release`.
3. Test the modified functionality on Windows.
4. Provide clear reproduction steps for bug reports.

For substantial changes, please open an issue first to discuss the proposed implementation.

## Bug Reports and Feature Requests

Please use the [GitHub Issues](https://github.com/aziizfarhat/DeskNest/issues) page to report bugs or suggest improvements.

## License

DeskNest is released under the [GNU General Public License v3.0](https://www.gnu.org/licenses/gpl-3.0.html) (GPL-3.0).

You may use, study, share and modify the software under the terms of the GPL-3.0. If you distribute modified versions, you must release them under the same license and make the corresponding source code available. The software is provided without warranty.

See [LICENSE](LICENSE) for the full legal text.
