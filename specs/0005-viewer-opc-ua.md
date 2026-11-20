---
type: Specification
title: "Spec 0005: OPC UA report in the viewer"
description: "How TIA Viewer shows each PLC's OPC UA report next to its blocks and tag tables."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-20T12:00:00Z }
status: stable
---
# Spec 0005: OPC UA report in the viewer

## Intent

Someone who opens an archive in TIA Viewer sees each PLC's OPC UA report (spec 0004) without the command line: whether the PLC's own OPC UA server is on, how it is set up, its roles, and which data blocks and tags an OPC UA client could reach. It is one more entry under the PLC in the tree, read the way a block is read, with the same marks on what is not yet confirmed against TIA.

## User scenarios

### See a PLC's OPC UA report (P1)

- Given an archive with a PLC of the project, when it is opened, then the PLC's node in the tree holds an entry "OPC UA".
- Given the entry is chosen, when it is shown, then the text is that PLC's OPC UA report as `tiadump opcua --plc <PLC>` writes it, read-only, with a heading that names it.
- Given a PLC with no OPC UA device item, when its entry is shown, then the report says so.
- Given a PLC held only in a master copy, when the archive is opened, then no OPC UA entry is shown for it.

### Copy it (P2)

- Given the report is shown, when Copy is chosen, then its text is copied.

### Keep it apart from the source (P2)

- Given the report is shown, when the Used by and Uses lists are shown, then they are empty.
- Given text that occurs only in a report, when all source is searched, then nothing is found.
- Given a PLC or the whole project is exported, when the files are written, then they are the same as without the report entry.
- Given the report entry, when its context menu or the Export button is shown, then it offers no export.

## Requirements

- FR-001: The tree MUST hold one entry "OPC UA" under each PLC of the project itself that the tree shows, and none under a master copy's PLC.
- FR-002: Choosing the entry MUST show the text `tiadump opcua --plc <PLC>` writes for that PLC, read-only.
- FR-003: The report MUST be made when the entry is first shown, not when the archive is opened.
- FR-004: Copy MUST copy the report's text.
- FR-005: The report MUST NOT count as an item of the project: it MUST NOT be searched, referenced, counted in the status line, or exported.
- FR-006: The entry MUST NOT offer an export.
- FR-007: The entry SHOULD carry a kind badge of its own.

## Success criteria

- SC-001: On the archives it was checked on, a person finds a PLC's OPC UA settings in the viewer within two clicks of opening it.
- SC-002: Opening an archive takes no longer than before, within measuring noise.

## Out of scope

- A view of the report other than its text: tables, filters, links from a data block's name to the block.
- Writing the report into an export.
- The README's pictures: they are taken on Windows from the demo project (`tools/ReadmePictures`).

## Decisions

None new. The report and its rules come from spec 0004.
