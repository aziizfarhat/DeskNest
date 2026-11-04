# DeskNest

**A lightweight desktop organizer for Windows.**

DeskNest helps you organize your Windows desktop into customizable zones and folder portals, keeping frequently used applications, files, and folders easier to access.

> **Project status:** Under active development. Features, settings, and the user interface may evolve.

## Features

### Desktop zones (Nests)

- Create desktop zones to group related shortcuts and items.
- Rename zones and select all items within a zone.
- Customize each zone's appearance:
  - Background color
  - Opacity
  - Icon size
  - Fonts
- Hide a zone's title when you prefer a cleaner desktop.
- Roll zones up, lock them, or expand them on hover.
- Delete zones when they are no longer needed.

### Folder Portals

- Create a Folder Portal to bring the contents of a folder onto your desktop in an organized area.
- Link a zone to a folder using the zone's context menu.

### Windows integration

- Access common zone actions from the zone context menu.
- Create a new zone or Folder Portal from the desktop background context menu.
- The desktop background menu is registered for the current Windows user while DeskNest is running and removed when the application exits normally.
- The desktop menu uses DeskNest's localization system.

### Multilingual interface

DeskNest includes translations for ten languages:

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

## Requirements

- Windows 10 or Windows 11
- .NET 8 Desktop Runtime (for running a framework-dependent build)

## Getting started

### Run from source

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Clone the repository:

   ```powershell
   git clone https://github.com/aziizfarhat/DeskNest.git
   cd DeskNest
   ```

3. Restore dependencies and build:

   ```powershell
   dotnet restore
   dotnet build
   ```

4. Run the application:

   ```powershell
   dotnet run
   ```

You can also launch the generated executable from the build output directory:

```text
bin\Debug\net8.0-windows\DeskNest.exe
```

## Usage

After launching DeskNest, create a zone from the application or by right-clicking an empty area of the desktop and choosing:

**DeskNest → New zone**

To create a Folder Portal, use:

**DeskNest → New Folder Portal...**

Right-click a zone to access its available actions and customization options.

## Technology

- C#
- .NET 8
- Windows Presentation Foundation (WPF)
- Windows Registry integration for the desktop background context menu
- Named Pipes for passing commands to the running application

## Contributing

Contributions, bug reports, and suggestions are welcome.

Before submitting a change:

1. Build the solution with `dotnet build`.
2. Test the affected feature on Windows 10 or Windows 11.
3. Include clear steps to reproduce any reported issue.

For substantial changes, please open an issue first to discuss the proposed implementation.

## Issues

If you encounter a bug or have a feature request, please use the
[GitHub Issues](https://github.com/aziizfarhat/DeskNest/issues) page.

## License

No license has been specified in this README. Please check the repository for the project's current licensing terms before redistributing or reusing the code.
