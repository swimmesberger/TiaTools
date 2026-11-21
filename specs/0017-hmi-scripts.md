---
type: Specification
title: "Spec 0017: HMI scripts, custom web controls and screen previews"
description: "What tiadump, the dump and the viewer must show of an HMI device's own code: Unified JavaScript, Comfort/Advanced VB scripts and custom web controls, with a schematic screen preview and a preview of a custom web control in the viewer."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T18:00:00Z }
status: stable
---
# Spec 0017: HMI scripts, custom web controls and screen previews

## Intent

Someone who has only a project's archive reads the code an HMI device runs besides the PLC program, without TIA Portal: every HMI script of a Unified device (on an event of a screen or screen item, on a property, and in global script modules), every VB script of a Comfort/Advanced device, and every custom web control with the HTML, CSS and JavaScript of its package. A coding agent gets the code as files from `tiadump` and the dump, as it gets SCL. A person reads it in the viewer as source, sees a screen as a schematic preview of its screen items, and can run a custom web control in a preview inside the viewer, when they ask for it. A Unified screen has no HTML of its own to extract: the runtime renders it from its screen items, which spec 0011 reads. This builds on [spec 0011](0011-hmi.md).

## User scenarios

### Read an HMI device's scripts (P1)

- Given a Unified screen item with an HMI script on an event, when the HMI report is made with scripts, then the script is listed with its screen, screen item, event and code.
- Given a Unified screen item with an HMI script on a property, when the report is made with scripts, then it is listed with its screen, screen item, property and code.
- Given a screen with an HMI script on one of its own events, when the report is made with scripts, then it is listed with its screen and event.
- Given a Unified device with a global script module, when the report is made with scripts, then the module is listed with its name and code.
- Given a Comfort/Advanced device with a VB script, when the report is made with scripts, then the script is listed with its name, its parameters and its code.
- Given an HMI device with no script, when the report is made with scripts, then it says so.

### Read the scripts as files (P1)

- Given an archive with HMI scripts, when it is dumped, then each HMI device's folder holds each script as a `.js` or `.vbs` file named after where it is attached, and each global script module as a file of its own.
- Given any archive, when the report with scripts is asked for as JSON, then it holds the same facts and the code of each script.

### Read a custom web control (P2)

- Given a Unified device with a custom web control on a screen, when the report is made with scripts, then the control is listed with its name, the screens and screen items it is used on, and the files of its package.
- Given such a control, when the archive is dumped, then the files of its package are written unpacked into the device's folder, as stored.

### Read code in the viewer (P2)

- Given an HMI script or a global script module is chosen, when it is shown, then its code is shown read-only, highlighted as JavaScript or VBScript.
- Given a screen item with HMI scripts in a screen's table, when the row's scripts are opened, then the viewer shows them.
- Given a custom web control is chosen, when it is shown, then the files of its package are listed, and each opens as read-only source.

### See a screen as a schematic preview (P2)

- Given a screen is chosen, when its preview is shown, then each screen item is drawn at its stored position and size, with its type, its text where it has one, and its bound HMI tag.
- Given an item in the preview is chosen, when it is shown, then its row in the screen's table is selected, and its scripts and PLC link can be opened.

### Preview a custom web control (P3)

- Given a custom web control is chosen, when the person asks for its preview, then the viewer runs the control's own files in a web view inside its window, and says that this runs the control's code and that no runtime values reach it.
- Given a custom web control is chosen, when the person has not asked for its preview, then none of its code runs.
- Given a system where the embedded web view is not available, when the person asks for the preview, then the viewer offers to open the control in the system browser instead.

### It agrees with TIA (P1)

- Given a reference project TIA Portal V21 built with HMI scripts on events and properties, global script modules, VB scripts and a custom web control, when the tests run, then the code of every script and every file of the control agrees with what TIA gave for it.

## Requirements

- FR-001: `tiadump hmi --scripts` MUST add to the HMI report every HMI script of a Unified device, with the screen, screen item where it has one, event or property, and code.
- FR-002: The report with scripts MUST list every global script module of a Unified device with its name and code.
- FR-003: The report with scripts MUST list every VB script of a Comfort/Advanced device with its name, parameters and code.
- FR-004: The report with scripts MUST list every custom web control of a Unified device with its name, where it is used, and the files of its package.
- FR-005: `tiadump dump` MUST write each HMI script, global script module and VB script as a `.js` or `.vbs` file in its HMI device's folder, named after where it is attached, and MUST write the files of each custom web control's package unpacked, as stored.
- FR-006: `tiadump hmi --scripts --json` MUST give the same facts as JSON, with each script's code.
- FR-007: The viewer MUST show each HMI script, global script module and VB script read-only, highlighted as its language, and MUST lead from a screen item's row to its scripts.
- FR-008: The viewer MUST draw a schematic preview of a screen: each screen item at its stored position and size, with its type, its text where it has one, and its bound HMI tag.
- FR-009: The viewer MUST NOT run a custom web control's code unless the person asks for its preview, and MUST say before it runs that it runs the control's own code without runtime values.
- FR-010: The preview of a custom web control MUST run in a web view inside the viewer's window, from a temporary folder that holds only that control's files and is removed when the preview closes.
- FR-011: The preview MUST NOT give the control's code access to the archive, the viewer or the PLC; it SHOULD block requests to the network where the web view allows it.
- FR-012: Where the embedded web view is not available, the viewer MUST offer to open the control's start page in the system browser instead, and MUST NOT fail.
- FR-013: A reference project MUST hold, built by TIA Portal V21 from invented sources, HMI scripts on an event and on a property of a screen item and on a screen, a global script module, a VB script, and a custom web control written for it.
- FR-014: The library's tests MUST compare the code of every script and the files of every custom web control of the reference project with what TIA gave for them, and a difference MUST fail the tests.
- FR-015: `docs/FORMAT.md` MUST describe where HMI scripts and custom web controls are stored, with its marks.
- FR-016: The reader MUST read a custom web control's package from the archive's `UserFiles/CustomControls` folder, where a project keeps it as a zip named after the control's GUID, and MUST list a package there that no screen uses.
- FR-017: What TIA V21's Openness cannot build, a custom web control placed on a screen, the reference project MUST get by hand in TIA's window, from steps written in its sources, and MUST say so where its build is described.

## Success criteria

- SC-001: An agent can answer what a screen item does when it is clicked from the dump alone, without TIA Portal.
- SC-002: Every script and custom web control file of the reference project comes out byte for byte as TIA gave it.
- SC-003: Opening an archive and reading its scripts never runs any of its code; only a person's explicit request runs a custom web control.

## Out of scope

- A faithful rendering of a screen: colours, styles, graphics, fonts and widgets as the runtime draws them. The preview is schematic.
- Running HMI scripts, or giving a previewed custom web control runtime values or the runtime's API.
- C scripts of WinCC Professional, and other WinCC families.
- Editing, compiling or checking scripts.
- Recipes, text and graphic lists, logging: spec 0011's out of scope still holds.

## Decisions

- [ADR 0017](../docs/adr/0017-custom-web-controls-run-in-the-platforms-web-view.md), a draft: a custom web control's preview runs in the platform's own web view through `Avalonia.Controls.WebView`.
- [ADR 0013](../docs/adr/0013-networks-are-shown-as-statements.md): the text the tools write is for an agent; the viewer is for people, so it shows source, tables and a drawn preview.
