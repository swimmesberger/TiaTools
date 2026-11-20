---
type: Specification
title: "Spec 0005: OPC UA view in the viewer"
description: "How TIA Viewer shows each PLC's OPC UA configuration, laid out the way TIA Portal shows it, next to the PLC's blocks and tag tables."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-20T12:00:00Z }
status: stable
---
# Spec 0005: OPC UA view in the viewer

## Intent

Someone who opens an archive in TIA Viewer sees each PLC's OPC UA configuration (spec 0004) without the command line, laid out the way TIA Portal shows it: the server's pages (General, Options, Security, User authentication), the server interfaces and roles, and the data blocks and tags with TIA's access columns. A data block or tag in the view leads to its source. What is inferred and not confirmed against TIA is marked where it is shown. The pages and labels follow the [research note](../docs/research/2026-11-20-tia-opc-ua-server-settings.md).

## User scenarios

### See a PLC's OPC UA server as TIA shows it (P1)

- Given an archive with a PLC of the project, when it is opened, then the PLC's node in the tree holds an entry "OPC UA" first.
- Given the entry is chosen, when it is shown, then the view says whether the server is on, and shows the sections General, Options, Security and User authentication with TIA's labels and the stored values.
- Given a setting the PLC does not store, such as the port, when its section is shown, then it reads "not stored" with TIA's default where the research note gives one.
- Given the security policies, when the Security section is shown, then each is a line with TIA's policy name, on or off or not stored, in TIA's order, marked as inferred, with its stored key at hand.
- Given a PLC with no OPC UA device item, when its entry is shown, then the view says so.
- Given a PLC held only in a master copy, when the archive is opened, then no OPC UA entry is shown for it.

### See what a client could reach (P1)

- Given the view is shown, when the data blocks section is shown, then each data block is a row with number, name, "DB accessible from OPC UA", and counts of closed and read-only members.
- Given the view is shown, when the tags section is shown, then each tag is a row with table, name, type, address, and the columns accessible, writable and visible, marked as inferred.
- Given a data block or tag row, when it is opened, then the viewer shows that data block or the tag in its table, and Back returns to the view.
- Given the server is off, when the view is shown, then it says that no client reaches the PLC through its own server, and still shows what would be open.

### Server interfaces and roles (P2)

- Given a PLC's OPC UA storage, when the view is shown, then a section lists the server interfaces, whether the journals hold changes, the last compile and download, and each role with its permissions per namespace.

### Copy it as text (P2)

- Given the view is shown, when Copy is chosen, then the text `tiadump opcua --plc <PLC>` writes is copied.

### Keep it apart from the source (P2)

- Given text that occurs only in the OPC UA configuration, when all source is searched, then nothing is found.
- Given a PLC or the whole project is exported, when the files are written, then they are the same as without the entry.
- Given the entry, when its context menu or the toolbar above it is shown, then it offers no export.

## Requirements

- FR-001: The tree MUST hold one entry "OPC UA", first, under each PLC of the project itself that the tree shows, and none under a master copy's PLC.
- FR-002: Choosing the entry MUST show a structured, read-only view of that PLC's OPC UA configuration, read when first shown, not when the archive is opened.
- FR-003: The view MUST show the server's settings in sections named and ordered as TIA's pages are (General, Options, Security, User authentication), with TIA's labels from the research note.
- FR-004: A setting that is not stored MUST read "not stored", and SHOULD give TIA's default where the research note gives one.
- FR-005: The security policies MUST be shown in TIA's order with TIA's names, each marked as inferred and with its stored key available.
- FR-006: The view MUST list every data block of the PLC with "DB accessible from OPC UA" and its counts of closed and read-only members, and every tag with its accessible, writable and visible settings.
- FR-007: Opening a data block or tag row MUST show that data block, or the tag's line in its table, as following a name in the source does.
- FR-008: Every inferred reading MUST be marked as such where it is shown.
- FR-009: The view MUST list server interfaces, journals, compile and download dates, and roles with their permissions.
- FR-010: Copy MUST copy the text `tiadump opcua --plc <PLC>` writes.
- FR-011: The OPC UA configuration MUST NOT count as an item of the project: it MUST NOT be searched, referenced, counted in the status line, or exported, and the entry MUST NOT offer an export.
- FR-012: The view MUST NOT show a password, key, hash or certificate, as spec 0004's FR-012 and FR-013 say.
- FR-013: The view MUST work with Native AOT: no reflection-based bindings.

## Success criteria

- SC-001: On the archives it was checked on, a person finds whether a PLC's OPC UA server is on, and which data blocks and tags it offers, within two clicks of opening the archive.
- SC-002: Opening an archive takes no longer than before, within measuring noise.
- SC-003: Someone who knows TIA Portal's OPC UA pages recognises each section without explanation.

## Out of scope

- Editing anything; the viewer stays read-only.
- Member-level detail of a data block in the view: its closed and read-only members are seen in the data block's own source.
- Sorting and filtering inside the view's lists.
- Writing the OPC UA configuration into an export.
- The README's pictures: they are taken on Windows from the demo project (`tools/ReadmePictures`).

## Decisions

None new. The report and its rules come from spec 0004; the layout from the research note. No new package: the lists are built from Avalonia's own controls, not a data grid package.
