---
type: Plan
title: "Plan 0003: viewer menus, About, recent files and file types"
description: "How spec 0003 is built in src/TiaViewer, and where it is tested."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-18T12:00:00Z }
status: draft
---
# Plan 0003: viewer menus, About, recent files and file types

## Modules

All of it is in `src/TiaViewer`; the library does not change.

- `MainWindow.Menu.cs` builds one `NativeMenu` for the main window. On macOS Avalonia exports it to the system's menu bar; elsewhere the `NativeMenuBar` at the top of `MainWindow.axaml` shows it in the window and hides itself on a Mac. Labels differ per platform only in case, access keys and Close Window against Exit. Each item calls the method the toolbar or context menu calls (`PickAndOpenAsync`, `PickAndExportAsync`, `CopySourceAsync`, AvaloniaEdit's own Find command).
- Shortcuts: a menu in the window shows gestures but does not run them, so the window's key handler runs the gesture of any enabled menu item. It does so on macOS too: the system menu takes a key before the window sees it, so nothing runs twice, but Avalonia 12 exports Shift+Cmd+F so that the system menu does not match it, and the key arrives at the window instead.
- Because the system menu takes Cmd+C, Cmd+A and Cmd+F before any control, Copy, Select All and Find act on the focused control themselves (FR-004).
- `App.axaml.cs` sets the app menu on macOS (About), renames Avalonia's "Quit" to "Quit TIA Viewer", gives Hide Others Option+Cmd+H instead of Avalonia's Option+Cmd+Q, and ends the app when the main window closes.
- `AboutWindow` shows `AppInfo`: the name, the informational version with its commit, and the repository link. The version is `<Version>` in `TiaViewer.csproj`.
- `RecentFiles` keeps the list as UTF-8 text, one full path per line, in `LocalApplicationData/TIA Viewer/recent.txt` (`~/Library/Application Support` on macOS, `%LOCALAPPDATA%` on Windows). Plain text needs no serializer, so it stays safe for Native AOT. Every change reads the file first and writes it through a temporary file (FR-010).
- `MacRecentDocuments` calls `NSDocumentController` through the Objective-C runtime with source-generated P/Invoke, since Avalonia has no API for it (FR-011).
- `TiaFileTypes` generates the extensions; the picker gets wildcards where the platform honours them and the explicit list on macOS, whose panel drops any pattern with a wildcard in the extension. `macos/Info.plist` lists the same extensions explicitly.

## Tests

`tests/TiaViewer.Tests`, on TUnit, needs no display:

- `RecentFilesTest`: the P1 scenarios of the recent files, FR-007, FR-008, FR-010.
- `TiaFileTypesTest`: FR-012, and FR-013 and FR-014 by reading `macos/Info.plist` and comparing it with `TiaFileTypes` and `AppInfo`.
- `AppInfoTest`: the version shown.

The menus, About and the open panel (FR-001 to FR-006, FR-009, FR-011) were checked by running the viewer on macOS, with a throwaway harness that read the system menu and the open panel through the Objective-C runtime and drove them, and by running it with the system menu turned off for the in-window bar. They were not checked on Windows.
