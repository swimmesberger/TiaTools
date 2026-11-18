---
type: Specification
title: "Spec 0003: viewer menus, About, recent files and file types"
description: "What the viewer's menu bar, About window, recent files and open dialog must do, on macOS and Windows."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-18T12:00:00Z }
status: draft
---
# Spec 0003: viewer menus, About, recent files and file types

## Intent

The viewer behaves like an app of the platform it runs on. It has a menu bar with the platform's names and shortcuts, an About window that says what it is and under which licence, the archives, projects and global libraries it opened last within reach, and an open dialog that offers the files of every TIA Portal version instead of greying some out.

## User scenarios

### Open again what I opened before (P1)

- Given two archives were opened, when File, Open Recent is shown, then both are listed, the last one first.
- Given an archive was opened and the viewer restarted, when the start screen is shown, then the archive is listed under Recent.
- Given the same archive is opened twice, when the list is shown, then it is listed once, first.
- Given eleven files were opened, when the list is shown, then it holds the ten most recent.
- Given a listed file was deleted, when the list is shown, then it is not listed.
- Given two listed files have the same name, when Open Recent is shown, then each also names the folder it is in.
- Given two viewers are open, when each opens a file, then the list holds both files.
- Given files are listed, when Clear Menu is chosen, then the list is empty, also after a restart.

### Choose any TIA Portal file in the open dialog (P1)

- Given an archive of V18 or of V15.1 on a Mac, when the open dialog is shown, then the file can be chosen.
- Given a project or global library of any version from V11 to V30, with or without the service pack suffix `_1`, when the open dialog is shown, then the file can be chosen.
- Given the Mac app is installed, when such a file is double-clicked, then the viewer is offered to open it.

### Work from the menu bar (P2)

- Given the viewer runs on a Mac, when its menus are shown, then the app menu holds About TIA Viewer and Quit TIA Viewer, File holds Open…, Open Recent, Export All…, Export Block… and Close Window, and Edit holds Copy, Select All, Find in Block… and Search All Source…, with Cmd shortcuts.
- Given the viewer runs on Windows, when its window is shown, then a menu bar in it holds File, Edit and Help, with Ctrl shortcuts and Exit in File.
- Given text is selected in the search box, when Cmd or Ctrl + C is pressed, then that text is copied.
- Given the tree has the focus and a block is shown, when Copy is chosen, then the block's source is copied, as the Copy button does.
- Given any part of the window has the focus and a block is shown, when Cmd or Ctrl + F is pressed, then the block's find bar opens.
- Given any part of the window has the focus, when Cmd or Ctrl + Shift + F is pressed, then the search box for all source has the focus.

### See what the viewer is (P3)

- When About TIA Viewer is chosen, then a window shows the name, the version, the icon, what the viewer does and its licence, once however often it is chosen.

## Requirements

The key words MUST, SHOULD and MAY are to be interpreted as described in RFC 2119 and RFC 8174.

- FR-001: The viewer MUST show a menu bar: on macOS the system's, with the app menu, File and Edit; on other platforms one in the window, with File, Edit and Help.
- FR-002: Menu items MUST use the platform's names and capitalisation and the platform's command key: Cmd on macOS, Ctrl elsewhere.
- FR-003: A menu item MUST do what the toolbar button, context menu item or shortcut of the same command does.
- FR-004: Copy and Select All MUST act on the text that has the focus; Copy with no text focused MUST copy the source of the block shown.
- FR-005: The shortcuts of the menu items MUST work wherever the focus is in the main window, unless the focused text box or editor has its own use for the key.
- FR-006: About MUST show the name, the version, the app icon, what the viewer does and the licence, from the app menu on macOS and from Help elsewhere.
- FR-007: The viewer MUST remember each archive, project, global library or folder it opened, most recent first, at most ten, each once, across restarts, in the platform's per-user application data folder.
- FR-008: Files that no longer exist MUST NOT be listed.
- FR-009: The list MUST be shown in File, Open Recent, which MUST end with Clear Menu, and on the start screen while nothing is open.
- FR-010: Two viewers running at once MUST NOT remove each other's entries.
- FR-011: On macOS the viewer SHOULD also add what it opened to the system's recent documents, and Clear Menu SHOULD clear those too.
- FR-012: The open dialog MUST accept archives, projects and global libraries of TIA Portal V11 to V30, each with and without the suffix `_1`, and `PEData.plf`, on every platform.
- FR-013: The Mac app MUST register the same extensions for opening by double-click as the open dialog accepts.
- FR-014: The version About shows MUST equal the version the Mac app declares.

## Success criteria

- SC-001: A file opened in an earlier session is open again within two clicks, from the start screen or the menu.
- SC-002: No archive, project or global library of a released TIA Portal version is greyed out in the open dialog on a Mac.

## Out of scope

- Opening files the reader does not open today, such as compressed global libraries.
- A file association, jump list or recent documents on Windows.
- Settings, pinning recent files, and a View or Window menu.
